namespace EasyDotnet.IntegrationTests.Roslyn;

/// <summary>
/// Two-project EF Core layout: the DbContext lives in a class library (Data) while the
/// design package and runtime configuration live in a separate startup project (Api).
/// </summary>
public class TempEfCoreSolution : IDisposable
{
  public string RootDir { get; }
  public string SlnxPath => Path.Combine(RootDir, "Fixture.slnx");
  public string DataCsprojPath => Path.Combine(RootDir, "Data", "Data.csproj");
  public string ApiCsprojPath => Path.Combine(RootDir, "Api", "Api.csproj");
  public string QueriesCsPath => Path.Combine(RootDir, "Data", "Queries.cs");

  public TempEfCoreSolution()
  {
    RootDir = Path.Combine(Path.GetTempPath(), $"EfSqlSolution_{Guid.NewGuid()}");
    Directory.CreateDirectory(Path.Combine(RootDir, "Data"));
    Directory.CreateDirectory(Path.Combine(RootDir, "Api"));
    CreateSolution();
  }

  private void CreateSolution()
  {
    File.WriteAllText(SlnxPath, """
        <Solution>
          <Project Path="Data/Data.csproj" />
          <Project Path="Api/Api.csproj" />
        </Solution>
        """);

    File.WriteAllText(DataCsprojPath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.*" />
          </ItemGroup>
        </Project>
        """);

    File.WriteAllText(QueriesCsPath, """
        using Microsoft.EntityFrameworkCore;

        namespace Fixture.Data;

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

        public static class Queries
        {
          public static async Task<List<Blog>> GetBlogsAsync(AppDbContext db, int minId, CancellationToken cancellationToken) =>
            await db.Blogs.Where(b => b.Id > minId).ToListAsync(cancellationToken);
        }
        """);

    File.WriteAllText(ApiCsprojPath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.*" />
            <ProjectReference Include="..\Data\Data.csproj" />
          </ItemGroup>
        </Project>
        """);

    File.WriteAllText(Path.Combine(RootDir, "Api", "Program.cs"), """
        Console.WriteLine("api");
        """);
  }

  /// <summary>
  /// Mirrors SettingsFileResolver's naming scheme so tests can await the background
  /// solution-load settings write before mutating settings (the store is last-writer-wins).
  /// </summary>
  public string SettingsFilePath
  {
    get
    {
      var normalized = Path.GetFullPath(SlnxPath).ToLowerInvariant();
      var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
      var dataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      return Path.Combine(dataPath, "easy-dotnet", $"solution_{hash}.json");
    }
  }

  /// <summary>
  /// Returns a 0-based (line, character) position on the first occurrence of <paramref name="marker"/> in Queries.cs.
  /// </summary>
  public (int Line, int Character) GetCursor(string marker)
  {
    var lines = File.ReadAllLines(QueriesCsPath);
    var line = Array.FindIndex(lines, x => x.Contains(marker));
    return (line, lines[line].IndexOf(marker));
  }

  public void Dispose()
  {
    // initialize sets the server's cwd to RootDir; move out before deleting it
    Directory.SetCurrentDirectory(Path.GetTempPath());
    if (Directory.Exists(RootDir))
    {
      Directory.Delete(RootDir, recursive: true);
    }
  }
}