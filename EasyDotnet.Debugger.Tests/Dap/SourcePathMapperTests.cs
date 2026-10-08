using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.Tests.Dap;

public class SourcePathMapperTests
{
  [Test]
  public async Task MapDebuggerToClientMapsSourceBelowConfiguredPrefix()
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(new Dictionary<string, string>
    {
      ["Modeler"] = "/work/modeler"
    });

    var result = mapper.MapDebuggerToClient("Modeler/Program.cs");

    await Assert.That(result).IsEqualTo("/work/modeler/Program.cs");
  }

  [Test]
  public async Task MapClientToDebuggerMapsSourceBelowConfiguredPrefix()
  {
    var mapper = CreateMapper(("Modeler", "/work/modeler"));

    var result = mapper.MapClientToDebugger("/work/modeler/Program.cs");

    await Assert.That(result).IsEqualTo("Modeler/Program.cs");
  }

  [Test]
  public async Task MapDebuggerToClientMapsExactPrefix()
  {
    var mapper = CreateMapper(("Modeler/", "/work/modeler/"));

    var clientPath = mapper.MapDebuggerToClient("Modeler");
    var debuggerPath = mapper.MapClientToDebugger("/work/modeler");

    await Assert.That(clientPath).IsEqualTo("/work/modeler");
    await Assert.That(debuggerPath).IsEqualTo("Modeler");
  }

  [Test]
  public async Task MappingRequiresPathComponentBoundary()
  {
    var mapper = CreateMapper(("Project", "/work/Project"));

    var debuggerPath = mapper.MapDebuggerToClient("Project.Other/Program.cs");
    var clientPath = mapper.MapClientToDebugger("/work/Project.Other/Program.cs");

    await Assert.That(debuggerPath).IsEqualTo("Project.Other/Program.cs");
    await Assert.That(clientPath).IsEqualTo("/work/Project.Other/Program.cs");
  }

  [Test]
  public async Task MappingUsesLongestPrefixInBothDirections()
  {
    var mapper = CreateMapper(
      ("src", "/work"),
      ("src/Project", "/work/Project"));

    var clientPath = mapper.MapDebuggerToClient("src/Project/Program.cs");
    var debuggerPath = mapper.MapClientToDebugger("/work/Project/Program.cs");

    await Assert.That(clientPath).IsEqualTo("/work/Project/Program.cs");
    await Assert.That(debuggerPath).IsEqualTo("src/Project/Program.cs");
  }

  [Test]
  public async Task MappingMatchesEitherSeparatorAndUsesDestinationStyle()
  {
    var mapper = CreateMapper((@"Modeler\Generated", @"C:\work\Modeler"));

    var clientPath = mapper.MapDebuggerToClient("Modeler/Generated/Program.cs");
    var debuggerPath = mapper.MapClientToDebugger("C:/work/Modeler/Program.cs");

    await Assert.That(clientPath).IsEqualTo(@"C:\work\Modeler\Program.cs");
    await Assert.That(debuggerPath).IsEqualTo(@"Modeler\Generated\Program.cs");
  }

  [Test]
  public async Task MappingLeavesUnmatchedPathsUnchanged()
  {
    var mapper = CreateMapper(("Modeler", "/work/modeler"));

    var debuggerPath = mapper.MapDebuggerToClient("Other/Program.cs");
    var clientPath = mapper.MapClientToDebugger("/tmp/Program.cs");

    await Assert.That(debuggerPath).IsEqualTo("Other/Program.cs");
    await Assert.That(clientPath).IsEqualTo("/tmp/Program.cs");
  }

  [Test]
  public async Task MappingUsesPlatformPathCaseRules()
  {
    var mapper = CreateMapper(("Modeler", "/work/modeler"));

    var result = mapper.MapDebuggerToClient("modeler/Program.cs");

    var expected = OperatingSystem.IsWindows()
      ? "/work/modeler/Program.cs"
      : "modeler/Program.cs";
    await Assert.That(result).IsEqualTo(expected);
  }

  [Test]
  public void ConfigureRejectsAmbiguousClientPrefixes()
  {
    var mapper = new SourcePathMapper();

    Assert.Throws<ArgumentException>(() => mapper.Configure(new Dictionary<string, string>
    {
      ["First"] = "/work/modeler/",
      ["Second"] = @"\work\modeler"
    }));
  }

  [Test]
  public async Task ConfigureWithEmptyMappingsClearsPreviousMappings()
  {
    var mapper = CreateMapper(("Modeler", "/work/modeler"));

    mapper.Configure(new Dictionary<string, string>());

    await Assert.That(mapper.MapDebuggerToClient("Modeler/Program.cs")).IsEqualTo("Modeler/Program.cs");
    await Assert.That(mapper.MapClientToDebugger("/work/modeler/Program.cs")).IsEqualTo("/work/modeler/Program.cs");
  }

  [Test]
  public void ConfigureRejectsNestedClientPrefixesForUnrelatedDebuggerPrefixes()
  {
    var mapper = new SourcePathMapper();

    Assert.Throws<ArgumentException>(() => mapper.Configure(new Dictionary<string, string>
    {
      ["src/First"] = "/work",
      ["src/Second"] = "/work/Nested"
    }));
  }

  [Test]
  public void ConfigureRejectsNestedDebuggerPrefixesForUnrelatedClientPrefixes()
  {
    var mapper = new SourcePathMapper();

    Assert.Throws<ArgumentException>(() => mapper.Configure(new Dictionary<string, string>
    {
      ["src"] = "/work/First",
      ["src/Nested"] = "/work/Second"
    }));
  }

  [Test]
  public async Task ConfigureAcceptsAlignedNestedPrefixes()
  {
    var mapper = CreateMapper(
      ("src", "/work"),
      ("src/Nested", "/work/Nested"));

    await Assert.That(mapper.MapDebuggerToClient("src/Nested/Program.cs"))
      .IsEqualTo("/work/Nested/Program.cs");
    await Assert.That(mapper.MapClientToDebugger("/work/Nested/Program.cs"))
      .IsEqualTo("src/Nested/Program.cs");
  }

  [Test]
  public async Task ConfigureAcceptsUnrelatedSiblingPrefixes()
  {
    var mapper = CreateMapper(
      ("src/First", "/work/First"),
      ("src/Second", "/work/Second"));

    await Assert.That(mapper.MapDebuggerToClient("src/Second/Program.cs"))
      .IsEqualTo("/work/Second/Program.cs");
  }

  private static SourcePathMapper CreateMapper(params (string Debugger, string Client)[] mappings)
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(mappings.ToDictionary(mapping => mapping.Debugger, mapping => mapping.Client));
    return mapper;
  }
}