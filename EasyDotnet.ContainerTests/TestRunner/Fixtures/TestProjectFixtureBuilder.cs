using System.Diagnostics;
using System.Text;

namespace EasyDotnet.ContainerTests.TestRunner.Fixtures;

/// <summary>
/// Fluent builder for a single real test project materialised under <c>/tmp</c> and
/// <b>built on the host</b> before container startup. The container then runs
/// <c>dotnet test --no-build --no-restore</c> against the prebuilt assembly.
/// <para>
/// <c>/tmp</c> is bind-mounted host↔container at identical absolute paths, so
/// <c>bin/Debug/&lt;tfm&gt;/*.dll</c> is visible inside the container at the same path.
/// </para>
/// <example>
/// <code>
/// using var fixture = new TestProjectFixtureBuilder()
///     .WithName("Mst.Sample")
///     .WithFramework(TestFrameworkKind.MsTestVsTest)
///     .WithNamespace("Mst.Sample.Alpha", ns => ns
///         .WithClass("Class1", c => c.WithTestMethod("Passing")))
///     .WithFile("MultiNs.cs", TestFixtures.MsTestBlockNamespaces)
///     .Build();
/// </code>
/// </example>
/// </summary>
public sealed class TestProjectFixtureBuilder
{
  private string _projectName = "TestFixture";
  private TestFrameworkKind? _framework;
  private bool _writeSolution = true;
  private readonly List<NamespaceSpec> _namespaces = [];
  private readonly List<(string RelativePath, string Content)> _extraFiles = [];

  public TestProjectFixtureBuilder WithName(string projectName)
  {
    _projectName = projectName;
    return this;
  }

  public TestProjectFixtureBuilder WithFramework(TestFrameworkKind framework)
  {
    _framework = framework;
    return this;
  }

  /// <summary>Skip writing a <c>.slnx</c> wrapper around the project. Defaults to writing one.</summary>
  public TestProjectFixtureBuilder WithoutSolution()
  {
    _writeSolution = false;
    return this;
  }

  /// <summary>
  /// Declares a namespace with zero or more classes. Each namespace is emitted to its own
  /// <c>&lt;LastSegment&gt;.cs</c> file using file-scoped syntax.
  /// </summary>
  public TestProjectFixtureBuilder WithNamespace(string fullName, Action<NamespaceBuilder> configure)
  {
    var builder = new NamespaceBuilder();
    configure(builder);
    _namespaces.Add(new NamespaceSpec(fullName, builder.Classes));
    return this;
  }

  /// <summary>
  /// Writes a verbatim <c>.cs</c> file into the project directory. Use for edge-case
  /// fixtures that can't be expressed via <see cref="WithNamespace"/> (e.g. block namespaces).
  /// </summary>
  public TestProjectFixtureBuilder WithFile(string relativePath, string content)
  {
    _extraFiles.Add((relativePath, content));
    return this;
  }

  /// <summary>Materialises the fixture to disk and runs <c>dotnet build</c> on the host.</summary>
  public TestProjectFixture Build()
  {
    if (_framework is null)
      throw new InvalidOperationException("WithFramework(...) must be called before Build().");

    var root = Path.Combine(Path.GetTempPath(), $"TestProjectFixture_{Guid.NewGuid():N}");
    var projectDir = Path.Combine(root, _projectName);
    Directory.CreateDirectory(projectDir);

    var csprojPath = Path.Combine(projectDir, $"{_projectName}.csproj");
    File.WriteAllText(csprojPath, RenderCsproj(_framework.Value));

    foreach (var ns in _namespaces)
    {
      var lastSegment = ns.FullName.Split('.').Last();
      var path = Path.Combine(projectDir, $"{lastSegment}.cs");
      File.WriteAllText(path, RenderNamespace(ns, _framework.Value));
    }

    foreach (var (relativePath, content) in _extraFiles)
    {
      var absolute = Path.Combine(projectDir, relativePath);
      Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
      File.WriteAllText(absolute, content);
    }

    string? solutionPath = null;
    if (_writeSolution)
    {
      solutionPath = Path.Combine(root, $"{_projectName}.slnx");
      File.WriteAllText(solutionPath, $"""
        <Solution>
          <Project Path="{csprojPath}" />
        </Solution>
        """);
    }

    RunDotnetBuild(csprojPath);

    return new TestProjectFixture(root, projectDir, csprojPath, solutionPath);
  }

  private static string RenderCsproj(TestFrameworkKind framework) => framework switch
  {
    TestFrameworkKind.MsTestVsTest => """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <IsPackable>false</IsPackable>
          <GenerateProgramFile>true</GenerateProgramFile>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
          <PackageReference Include="MSTest.TestAdapter" Version="3.6.3" />
          <PackageReference Include="MSTest.TestFramework" Version="3.6.3" />
        </ItemGroup>
      </Project>
      """,
    TestFrameworkKind.TUnitMtp => """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <OutputType>Exe</OutputType>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <IsPackable>false</IsPackable>
          <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="TUnit" Version="1.13.56" />
        </ItemGroup>
      </Project>
      """,
    TestFrameworkKind.XUnitV2VsTest => """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <IsPackable>false</IsPackable>
          <GenerateProgramFile>true</GenerateProgramFile>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
          <PackageReference Include="xunit" Version="2.9.2" />
          <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
        </ItemGroup>
      </Project>
      """,
    TestFrameworkKind.XUnitV3Mtp => """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <OutputType>Exe</OutputType>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <IsPackable>false</IsPackable>
          <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="xunit.v3" Version="1.0.1" />
        </ItemGroup>
      </Project>
      """,
    TestFrameworkKind.NUnitV3VsTest => """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <IsPackable>false</IsPackable>
          <GenerateProgramFile>true</GenerateProgramFile>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
          <PackageReference Include="NUnit" Version="3.14.0" />
          <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
        </ItemGroup>
      </Project>
      """,
    TestFrameworkKind.NUnitV4Mtp => """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <OutputType>Exe</OutputType>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <IsPackable>false</IsPackable>
          <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
          <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
          <PackageReference Include="NUnit" Version="4.2.2" />
          <PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
        </ItemGroup>
      </Project>
      """,
    _ => throw new NotImplementedException($"TestFrameworkKind.{framework} is not implemented yet."),
  };

  private static string RenderNamespace(NamespaceSpec ns, TestFrameworkKind framework) => framework switch
  {
    TestFrameworkKind.MsTestVsTest => RenderMsTestNamespace(ns),
    TestFrameworkKind.TUnitMtp => RenderTUnitNamespace(ns),
    TestFrameworkKind.XUnitV2VsTest or TestFrameworkKind.XUnitV3Mtp => RenderXUnitNamespace(ns),
    TestFrameworkKind.NUnitV3VsTest or TestFrameworkKind.NUnitV4Mtp => RenderNUnitNamespace(ns),
    _ => throw new NotImplementedException($"TestFrameworkKind.{framework} is not implemented yet."),
  };

  private static string RenderMsTestNamespace(NamespaceSpec ns)
  {
    var sb = new StringBuilder();
    sb.AppendLine("using Microsoft.VisualStudio.TestTools.UnitTesting;");
    sb.AppendLine();
    sb.AppendLine($"namespace {ns.FullName};");
    foreach (var cls in ns.Classes)
    {
      sb.AppendLine();
      sb.AppendLine("[TestClass]");
      sb.AppendLine($"public class {cls.Name}");
      sb.AppendLine("{");
      var first = true;
      foreach (var method in cls.Methods)
      {
        if (!first) sb.AppendLine();
        first = false;
        sb.AppendLine("    [TestMethod]");
        sb.AppendLine($"    public void {method}() {{ }}");
      }
      sb.AppendLine("}");
    }
    return sb.ToString();
  }

  private static string RenderXUnitNamespace(NamespaceSpec ns)
  {
    var sb = new StringBuilder();
    sb.AppendLine("using Xunit;");
    sb.AppendLine();
    sb.AppendLine($"namespace {ns.FullName};");
    foreach (var cls in ns.Classes)
    {
      sb.AppendLine();
      sb.AppendLine($"public class {cls.Name}");
      sb.AppendLine("{");
      var first = true;
      foreach (var method in cls.Methods)
      {
        if (!first) sb.AppendLine();
        first = false;
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public void {method}() {{ }}");
      }
      sb.AppendLine("}");
    }
    return sb.ToString();
  }

  private static string RenderNUnitNamespace(NamespaceSpec ns)
  {
    var sb = new StringBuilder();
    sb.AppendLine("using NUnit.Framework;");
    sb.AppendLine();
    sb.AppendLine($"namespace {ns.FullName};");
    foreach (var cls in ns.Classes)
    {
      sb.AppendLine();
      sb.AppendLine($"public class {cls.Name}");
      sb.AppendLine("{");
      var first = true;
      foreach (var method in cls.Methods)
      {
        if (!first) sb.AppendLine();
        first = false;
        sb.AppendLine("    [Test]");
        sb.AppendLine($"    public void {method}() {{ }}");
      }
      sb.AppendLine("}");
    }
    return sb.ToString();
  }

  private static string RenderTUnitNamespace(NamespaceSpec ns)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"namespace {ns.FullName};");
    foreach (var cls in ns.Classes)
    {
      sb.AppendLine();
      sb.AppendLine($"public class {cls.Name}");
      sb.AppendLine("{");
      var first = true;
      foreach (var method in cls.Methods)
      {
        if (!first) sb.AppendLine();
        first = false;
        sb.AppendLine("    [Test]");
        sb.AppendLine($"    public async Task {method}() => await Task.CompletedTask;");
      }
      sb.AppendLine("}");
    }
    return sb.ToString();
  }

  private static void RunDotnetBuild(string csprojPath)
  {
    var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Debug --nologo")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    using var process = Process.Start(psi)
      ?? throw new InvalidOperationException("Failed to start `dotnet build`.");

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
      throw new InvalidOperationException(
        $"`dotnet build` failed for fixture project '{csprojPath}' with exit code {process.ExitCode}.\n" +
        $"stdout:\n{stdout}\nstderr:\n{stderr}");
  }

  private sealed record NamespaceSpec(string FullName, IReadOnlyList<ClassSpec> Classes);
  internal sealed record ClassSpec(string Name, IReadOnlyList<string> Methods);

  public sealed class NamespaceBuilder
  {
    internal List<ClassSpec> Classes { get; } = [];

    public NamespaceBuilder WithClass(string name, Action<ClassBuilder> configure)
    {
      var builder = new ClassBuilder();
      configure(builder);
      Classes.Add(new ClassSpec(name, builder.Methods));
      return this;
    }
  }

  public sealed class ClassBuilder
  {
    internal List<string> Methods { get; } = [];

    public ClassBuilder WithTestMethod(string name)
    {
      Methods.Add(name);
      return this;
    }
  }
}

/// <summary>
/// A materialised, host-built test project. Dispose to delete the temp directory.
/// </summary>
public sealed class TestProjectFixture : IDisposable
{
  public string RootDir { get; }
  public string ProjectDir { get; }
  public string CsprojPath { get; }
  public string? SolutionPath { get; }

  internal TestProjectFixture(string rootDir, string projectDir, string csprojPath, string? solutionPath)
  {
    RootDir = rootDir;
    ProjectDir = projectDir;
    CsprojPath = csprojPath;
    SolutionPath = solutionPath;
  }

  public void Dispose()
  {
    if (Directory.Exists(RootDir))
    {
      try { Directory.Delete(RootDir, recursive: true); }
      catch { /* best-effort cleanup */ }
    }
  }
}