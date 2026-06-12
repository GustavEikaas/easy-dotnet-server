namespace EasyDotnet.IntegrationTests.Roslyn;

public class TempEfCoreProject : IDisposable
{
  public string ProjectDirectory { get; }
  public string CsprojPath => Path.Combine(ProjectDirectory, "EfQueryFixture.csproj");
  public string ProgramCsPath => Path.Combine(ProjectDirectory, "Program.cs");

  public TempEfCoreProject()
  {
    ProjectDirectory = Path.Combine(Path.GetTempPath(), $"EfQueryFixture_{Guid.NewGuid()}");
    Directory.CreateDirectory(ProjectDirectory);
    CreateProject();
  }

  private void CreateProject()
  {
    var csprojContent = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.*" />
            <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.*" />
          </ItemGroup>
        </Project>
        """;

    File.WriteAllText(CsprojPath, csprojContent);

    var programCode = """
        using Microsoft.EntityFrameworkCore;

        namespace Fixture;

        public record BlogTitleDto(string Title);

        public class Blog
        {
          public int Id { get; set; }
          public string Title { get; set; } = "";
        }

        public class AppDbContext : DbContext
        {
          public DbSet<Blog> Blogs => Set<Blog>();

          protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=app.db");
        }

        public class Program
        {
          public static void Main()
          {
            var minId = 5;
            using var db = new AppDbContext();
            var query = db.Blogs.Where(b => b.Id > minId).OrderBy(b => b.Title);
            Console.WriteLine(query.Count());
          }

          public static async Task<List<Blog>> GetBlogsAsync(AppDbContext appDbContext, int minId, CancellationToken cancellationToken)
          {
            var blogs = await appDbContext.Blogs
                .Where(b => b.Id > minId)
                .ToListAsync(cancellationToken);
            return blogs;
          }

          public static IQueryable<BlogTitleDto> ProjectTitles(AppDbContext db, int minId) =>
            db.Blogs.Where(b => b.Id > minId).Select(b => new BlogTitleDto(b.Title));

          public record NestedDto(int Id, string Title);

          public static IQueryable<NestedDto> ProjectNested(AppDbContext db, int minId) =>
            db.Blogs.Where(b => b.Id > minId).Select(b => new NestedDto(b.Id, b.Title));
        }
        """;

    File.WriteAllText(ProgramCsPath, programCode);
  }

  /// <summary>
  /// Returns a 0-based (line, character) position on the first occurrence of <paramref name="marker"/> in Program.cs.
  /// </summary>
  public (int Line, int Character) GetCursor(string marker)
  {
    var lines = File.ReadAllLines(ProgramCsPath);
    var line = Array.FindIndex(lines, x => x.Contains(marker));
    return (line, lines[line].IndexOf(marker));
  }

  /// <summary>
  /// Returns a 0-based (line, character) position inside the LINQ query in Program.cs.
  /// </summary>
  public (int Line, int Character) GetQueryCursor()
  {
    var lines = File.ReadAllLines(ProgramCsPath);
    var line = Array.FindIndex(lines, x => x.Contains("db.Blogs.Where"));
    return (line, lines[line].IndexOf("Blogs"));
  }

  public void Dispose()
  {
    if (Directory.Exists(ProjectDirectory))
    {
      Directory.Delete(ProjectDirectory, recursive: true);
    }
  }
}