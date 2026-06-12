using EasyDotnet.RoslynLanguageServices.EfQuery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EasyDotnet.RoslynLanguageServices.Tests;

public sealed class EfQueryDetectorTests
{
  private const string EfStubs = """
      namespace Microsoft.EntityFrameworkCore
      {
        public class DbContext { }

        public class DbSet<TEntity> : System.Linq.IQueryable<TEntity> where TEntity : class
        {
          public System.Type ElementType => typeof(TEntity);
          public System.Linq.Expressions.Expression Expression => null!;
          public System.Linq.IQueryProvider Provider => null!;
          public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null!;
          System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
        }
      }
      """;

  private const string QueryCode = """
      using System.Linq;
      using Microsoft.EntityFrameworkCore;

      namespace App;

      public class Blog
      {
        public int Id { get; set; }
        public string Title { get; set; } = "";
      }

      public class AppDbContext : DbContext
      {
        public DbSet<Blog> Blogs { get; set; } = null!;
      }

      public class Queries
      {
        public IQueryable<string> GetTitles(AppDbContext db, int minId)
        {
          var titles = db.Blogs.Where(b => b.Id > minId).Select(b => b.Title);
          return titles;
        }
      }
      """;

  [Test]
  public async Task CursorInsideQueryChain_FindsQuery()
  {
    var detection = await Detect(QueryCode, "Where");

    await Assert.That(detection).IsNotNull();
    await Assert.That(detection!.QueryExpression.ToString()).StartsWith("db.Blogs.Where");
  }

  [Test]
  public async Task CursorOnVarKeyword_FindsQueryViaStatementFallback()
  {
    var detection = await Detect(QueryCode, "var titles");

    await Assert.That(detection).IsNotNull();
    await Assert.That(detection!.QueryExpression.ToString()).StartsWith("db.Blogs.Where");
  }

  [Test]
  public async Task CursorOutsideQuery_ReturnsNull()
  {
    var detection = await Detect(QueryCode, "return titles");

    await Assert.That(detection).IsNull();
  }

  [Test]
  public async Task NonEfQueryable_ReturnsNull()
  {
    const string code = """
        using System.Linq;

        public class Queries
        {
          public IQueryable<int> GetNumbers(int[] numbers) => numbers.AsQueryable().Where(x => x > 1);
        }
        """;

    var detection = await Detect(code, "Where");

    await Assert.That(detection).IsNull();
  }

  [Test]
  public async Task ContextNode_IsTheContextExpression()
  {
    var detection = await Detect(QueryCode, "Where");

    await Assert.That(detection!.ContextNodes.Count).IsEqualTo(1);
    await Assert.That(detection.ContextNodes[0].ToString()).IsEqualTo("db");
  }

  private static async Task<EfQueryDetection?> Detect(string code, string marker)
  {
    var document = CreateDocument(code);
    var text = await document.GetTextAsync();
    var position = text.ToString().IndexOf(marker, StringComparison.Ordinal);

    var root = await document.GetSyntaxRootAsync();
    var semanticModel = await document.GetSemanticModelAsync();

    return EfQueryDetector.FindQuery(root!, semanticModel!, position, CancellationToken.None);
  }

  private static Document CreateDocument(string code)
  {
    var workspace = new AdhocWorkspace();
    var projectInfo = ProjectInfo.Create(
        ProjectId.CreateNewId(),
        VersionStamp.Create(),
        "TestProject",
        "TestProject",
        LanguageNames.CSharp,
        parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
        compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
        metadataReferences:
        [
          MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
          MetadataReference.CreateFromFile(typeof(IQueryable<>).Assembly.Location),
          MetadataReference.CreateFromFile(typeof(Queryable).Assembly.Location),
          MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
          MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
        ]);

    var project = workspace.AddProject(projectInfo);
    project = project.AddDocument("EfStubs.cs", SourceText.From(EfStubs), filePath: "/tmp/EfStubs.cs").Project;
    return project.AddDocument("Queries.cs", SourceText.From(code), filePath: "/tmp/Queries.cs");
  }
}