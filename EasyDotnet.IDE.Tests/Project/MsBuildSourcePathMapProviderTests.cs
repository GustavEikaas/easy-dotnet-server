using System.Text.Json;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Debugger.Services;
using EasyDotnet.IDE.Project.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.IDE.Tests.Project;

public sealed class MsBuildSourcePathMapProviderTests
{
  [Test]
  public async Task GetMappingsReversesMultipleEvaluatedPathMapEntries()
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(
        ("First.csproj", @"/work/First=Mapped/First,C:\work\Second=Mapped\Second"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings.Count).IsEqualTo(2);
    await Assert.That(mappings["Mapped/First"]).IsEqualTo("/work/First");
    await Assert.That(mappings[@"Mapped\Second"]).IsEqualTo(@"C:\work\Second");
  }

  [Test]
  public async Task GetMappingsParsesDoubledCommaAndEqualsEscapes()
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot((
        "Escaped.csproj",
        "/work/with,,comma=Mapped/Comma,/work/with==equals=Mapped==Equals"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings.Count).IsEqualTo(2);
    await Assert.That(mappings["Mapped/Comma"]).IsEqualTo("/work/with,comma");
    await Assert.That(mappings["Mapped=Equals"]).IsEqualTo("/work/with=equals");
  }

  [Test]
  public async Task GetMappingsOmitsIdentityMappings()
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(("Identity.csproj", "/work/project=/work/project/"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings).IsEmpty();
  }

  [Test]
  public async Task GetMappingsSkipsMalformedUnsupportedAndEmptyEntries()
  {
    var logger = new CapturingLogger<MsBuildSourcePathMapProvider>();
    var provider = new MsBuildSourcePathMapProvider(logger);
    var snapshot = CreateSnapshot((
        "Malformed.csproj",
        "missing-separator,/work/Valid=Mapped,too=many=parts,=EmptyDebugger,/empty="));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings.Count).IsEqualTo(1);
    await Assert.That(mappings["Mapped"]).IsEqualTo("/work/Valid");
    await Assert.That(logger.Warnings.Count).IsEqualTo(4);
    await Assert.That(logger.Warnings.All(message => message.Contains("Skipping unsupported PathMap entry"))).IsTrue();
    await Assert.That(logger.Information).IsEquivalentTo([
      "Automatic MSBuild PathMap discovery: 1 evaluated projects, 1 with nonempty PathMap, 1 accepted mappings, 0 identity entries omitted, 4 entries skipped, 0 conflicts skipped"
    ]);
  }

  [Test]
  public async Task GetMappingsIgnoresEmptyOuterEntries()
  {
    var logger = new CapturingLogger<MsBuildSourcePathMapProvider>();
    var provider = new MsBuildSourcePathMapProvider(logger);
    var snapshot = CreateSnapshot(("Empty.csproj", ",/work/Valid=Mapped,"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings.Count).IsEqualTo(1);
    await Assert.That(mappings["Mapped"]).IsEqualTo("/work/Valid");
    await Assert.That(logger.Warnings.Count).IsEqualTo(0);
    await Assert.That(logger.Information).IsEquivalentTo([
      "Automatic MSBuild PathMap discovery: 1 evaluated projects, 1 with nonempty PathMap, 1 accepted mappings, 0 identity entries omitted, 0 entries skipped, 0 conflicts skipped"
    ]);
  }

  [Test]
  public async Task GetMappingsDeduplicatesEquivalentEntries()
  {
    var logger = new CapturingLogger<MsBuildSourcePathMapProvider>();
    var provider = new MsBuildSourcePathMapProvider(logger);
    var snapshot = CreateSnapshot(
        ("First.csproj", "/work/First=Mapped"),
        ("Second.csproj", "/work/First=Mapped,/work/First/=Mapped/"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings.Count).IsEqualTo(1);
    await Assert.That(mappings["Mapped"]).IsEqualTo("/work/First");
    await Assert.That(logger.Warnings.Count).IsEqualTo(0);
    await Assert.That(logger.Information).IsEquivalentTo([
      "Automatic MSBuild PathMap discovery: 2 evaluated projects, 2 with nonempty PathMap, 1 accepted mappings, 0 identity entries omitted, 2 entries skipped, 0 conflicts skipped"
    ]);
  }

  [Test]
  public async Task GetMappingsOmitsDebuggerPrefixMappedToDifferentPhysicalRoots()
  {
    var logger = new CapturingLogger<MsBuildSourcePathMapProvider>();
    var provider = new MsBuildSourcePathMapProvider(logger);
    var snapshot = CreateSnapshot(
        ("First.csproj", "/work/First=Mapped"),
        ("Second.csproj", "/work/Second=Mapped"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings).IsEmpty();
    await Assert.That(logger.Warnings).IsEquivalentTo([
      "Quarantining PathMap entry /work/Second=Mapped from Second.csproj with accepted mappings /work/First=Mapped; debugger or physical prefix regions overlap"
    ]);
    await Assert.That(logger.Information).IsEquivalentTo([
      "Automatic MSBuild PathMap discovery: 2 evaluated projects, 2 with nonempty PathMap, 0 accepted mappings, 0 identity entries omitted, 0 entries skipped, 2 conflicts skipped"
    ]);
  }

  [Test]
  [Arguments("/work/First=src,/work/Second=src/Nested")]
  [Arguments("/work/Second=src/Nested,/work/First=src")]
  [Arguments("/work/First=src,/work/First/Nested=other")]
  [Arguments("/work/First/Nested=other,/work/First=src")]
  public async Task GetMappingsQuarantinesAsymmetricPrefixConflictsInEitherOrder(string pathMap)
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(("Nested.csproj", pathMap));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings).IsEmpty();
  }

  [Test]
  [Arguments("/work=src,/work/Nested=src/Nested")]
  [Arguments("/work/Nested=src/Nested,/work=src")]
  public async Task GetMappingsQuarantinesSymmetricOverlapsInEitherCompilerOrder(string pathMap)
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(("Nested.csproj", pathMap));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings).IsEmpty();
  }

  [Test]
  public async Task ConflictingNestedDebuggerPrefixCannotFallThroughAcceptedAncestor()
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot((
        "Nested.csproj",
        "/work=src,/work/Nested=src/Nested,/other=src/Nested"));
    var mapper = new SourcePathMapper();

    mapper.Configure(provider.GetMappings(snapshot));

    await Assert.That(mapper.MapDebuggerToClient("src/Nested/File.cs"))
      .IsEqualTo("src/Nested/File.cs");
  }

  [Test]
  public async Task QuarantinedCandidateAlsoRemovesMappingsItOverlapsInTheOtherPrefixSpace()
  {
    var logger = new CapturingLogger<MsBuildSourcePathMapProvider>();
    var provider = new MsBuildSourcePathMapProvider(logger);
    var snapshot = CreateSnapshot((
        "Transitive.csproj",
        "/safe=clean,/q1=q,/q2=q/Nested,/safe/Nested=q/Other"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings).IsEmpty();
    await Assert.That(logger.Warnings.Count).IsEqualTo(2);
    await Assert.That(logger.Information).IsEquivalentTo([
      "Automatic MSBuild PathMap discovery: 1 evaluated projects, 1 with nonempty PathMap, 0 accepted mappings, 0 identity entries omitted, 0 entries skipped, 4 conflicts skipped"
    ]);
  }

  [Test]
  public async Task GetMappingsPreservesSeparatorsForSourcePathMapper()
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(("Windows.csproj", @"C:\repo\Project=Mapped\Project"));
    var mapper = new SourcePathMapper();

    mapper.Configure(provider.GetMappings(snapshot));

    await Assert.That(mapper.MapDebuggerToClient("Mapped/Project/File.cs"))
      .IsEqualTo(@"C:\repo\Project\File.cs");
    await Assert.That(mapper.MapClientToDebugger("C:/repo/Project/File.cs"))
      .IsEqualTo(@"Mapped\Project\File.cs");
  }

  [Test]
  public async Task GetMappingsUsesEvaluatedProjectOverrideValue()
  {
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(
        ("Default.csproj", "/repo/Default=src/Default"),
        ("Override.csproj", "/repo/Override=custom/ProjectName"));

    var mappings = provider.GetMappings(snapshot);

    await Assert.That(mappings["src/Default"]).IsEqualTo("/repo/Default");
    await Assert.That(mappings["custom/ProjectName"]).IsEqualTo("/repo/Override");
  }

  [Test]
  public async Task GetMappingsValidatesLargeMappingSetAndQuarantinesConflictingNesting()
  {
    var entries = Enumerable.Range(0, 200)
      .Select(index => $"/work/Project{index}=src/Project{index}")
      .Append("/other=src/Project0/Nested");
    var provider = CreateProvider();
    var snapshot = CreateSnapshot(("Large.csproj", string.Join(',', entries)));

    var mappings = provider.GetMappings(snapshot);
    var mapper = new SourcePathMapper();
    mapper.Configure(mappings);

    await Assert.That(mappings.Count).IsEqualTo(199);
    await Assert.That(mappings["src/Project199"]).IsEqualTo("/work/Project199");
    await Assert.That(mappings.ContainsKey("src/Project0")).IsFalse();
    await Assert.That(mappings.ContainsKey("src/Project0/Nested")).IsFalse();
    await Assert.That(mapper.MapDebuggerToClient("src/Project0/Nested/File.cs"))
      .IsEqualTo("src/Project0/Nested/File.cs");
  }

  private static MsBuildSourcePathMapProvider CreateProvider() =>
    new(NullLogger<MsBuildSourcePathMapProvider>.Instance);

  private static ProjectGraphSnapshot CreateSnapshot(params (string ProjectPath, string PathMap)[] projects) =>
    new(
        "/repo/App.sln",
        [],
        [.. projects.Select(project => CreateProject(project.ProjectPath, project.PathMap))],
        []);

  private static ValidatedDotnetProject CreateProject(string projectPath, string pathMap)
  {
    var raw = JsonSerializer.Deserialize<DotnetProject>(JsonSerializer.Serialize(new { PathMap = pathMap }))!;
    return new ValidatedDotnetProject
    {
      TargetFramework = "net8.0",
      OutputType = "Library",
      ProjectPath = projectPath,
      ProjectFullPath = projectPath,
      ProjectName = Path.GetFileNameWithoutExtension(projectPath),
      AssemblyName = Path.GetFileNameWithoutExtension(projectPath),
      TargetPath = Path.ChangeExtension(projectPath, ".dll"),
      Raw = raw
    };
  }

  private sealed class CapturingLogger<T> : ILogger<T>
  {
    public List<string> Warnings { get; } = [];
    public List<string> Information { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
      if (logLevel == LogLevel.Warning)
      {
        Warnings.Add(formatter(state, exception));
      }
      else if (logLevel == LogLevel.Information)
      {
        Information.Add(formatter(state, exception));
      }
    }
  }
}