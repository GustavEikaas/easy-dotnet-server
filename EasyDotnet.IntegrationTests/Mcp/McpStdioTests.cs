using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyDotnet.IntegrationTests.Mcp;

/// <summary>
/// Drives <c>easydotnet mcp</c> over real stdin/stdout with raw JSON-RPC. Every line the server
/// writes to stdout must be a protocol message: build and test child processes inherit the
/// server's handles, and one stray line would break the client.
/// </summary>
public sealed class McpStdioTests : IDisposable
{
  private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(4);

  private readonly string _root = Path.Combine(Path.GetTempPath(), "edmcp-" + Guid.NewGuid().ToString("N"));

  public McpStdioTests() => Directory.CreateDirectory(_root);

  [Fact]
  public async Task ToolsList_ExposesBuildAndTestTools()
  {
    WriteSolution("Lib", ClassLibProject, ("Ok.cs", "namespace Lib; public class Ok { }"));
    await using var server = await McpProcess.StartAsync(_root);

    var result = await server.CallAsync("tools/list", new JsonObject(), CallTimeout);

    var names = result["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
    Assert.Equal(["build", "get_test_result", "list_tests", "run_tests"], names.Order());
  }

  [Fact]
  public async Task Build_ProjectWithCompileError_ReturnsStructuredError()
  {
    WriteSolution("Lib", ClassLibProject, ("Broken.cs", "namespace Lib;\npublic class Broken\n{\n  public int Value => missing;\n}"));
    await using var server = await McpProcess.StartAsync(_root);

    var build = await server.CallToolAsync("build", new JsonObject(), CallTimeout);

    Assert.False(build["success"]!.GetValue<bool>());
    var error = Assert.Single(build["errors"]!.AsArray());
    Assert.Equal("CS0103", error!["code"]!.GetValue<string>());
    Assert.EndsWith("Broken.cs", error["file"]!.GetValue<string>());
    Assert.Equal(4, error["line"]!.GetValue<int>());
  }

  [Fact]
  public async Task RunTests_ByFileAndLine_RunsThatTestAndDescribesTheFailure()
  {
    const string cases = """
      using Xunit;

      namespace Sample;

      public class Cases
      {
          [Fact]
          public void Passes() => Assert.True(true);

          [Fact]
          public void Fails() => Assert.Equal(1, 2);
      }
      """;
    WriteSolution("Sample.Tests", XUnitProject, ("Cases.cs", cases));
    await using var server = await McpProcess.StartAsync(_root);
    var casesPath = Path.Combine(_root, "Sample.Tests", "Cases.cs");

    var run = await server.CallToolAsync("run_tests", new JsonObject { ["file"] = casesPath, ["line"] = 11 }, CallTimeout);

    Assert.False(run["success"]!.GetValue<bool>());
    Assert.Equal(1, run["total"]!.GetValue<int>());
    Assert.Equal(1, run["failed"]!.GetValue<int>());
    var failure = Assert.Single(run["failures"]!.AsArray());
    Assert.Equal("Sample.Cases.Fails", failure!["name"]!.GetValue<string>());
    Assert.Equal($"{casesPath}:11", failure["location"]!.GetValue<string>());

    var rerun = await server.CallToolAsync("run_tests", new JsonObject { ["onlyFailed"] = true }, CallTimeout);
    Assert.Equal(1, rerun["total"]!.GetValue<int>());

    var all = await server.CallToolAsync("list_tests", new JsonObject(), CallTimeout);
    Assert.Equal(2, all["total"]!.GetValue<int>());
  }

  [Fact]
  public async Task RunTests_WhenTestProjectDoesNotBuild_ReportsTheBuildErrors()
  {
    WriteSolution("Sample.Tests", XUnitProject, ("Cases.cs", "namespace Sample;\npublic class Cases { int Broken => missing; }"));
    await using var server = await McpProcess.StartAsync(_root);

    var run = await server.CallToolAsync("run_tests", new JsonObject(), CallTimeout);

    Assert.False(run["success"]!.GetValue<bool>());
    var project = Assert.Single(run["buildFailed"]!.AsArray());
    Assert.Equal("Sample.Tests", project!["project"]!.GetValue<string>());
    Assert.Contains(project["errors"]!.AsArray(), e => e!["code"]!.GetValue<string>() == "CS0103");
  }

  private void WriteSolution(string projectName, string csproj, params (string Name, string Content)[] files)
  {
    var projectDir = Path.Combine(_root, projectName);
    Directory.CreateDirectory(projectDir);
    File.WriteAllText(Path.Combine(projectDir, projectName + ".csproj"), csproj);
    foreach (var (name, content) in files)
    {
      File.WriteAllText(Path.Combine(projectDir, name), content);
    }
    File.WriteAllText(Path.Combine(_root, "Sample.slnx"), $"""
      <Solution>
        <Project Path="{projectName}/{projectName}.csproj" />
      </Solution>
      """);
  }

  private const string ClassLibProject = """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
      </PropertyGroup>
    </Project>
    """;

  private const string XUnitProject = """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <IsPackable>false</IsPackable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
        <PackageReference Include="xunit" Version="2.9.2" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
      </ItemGroup>
    </Project>
    """;

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); } catch { }
  }

  private sealed class McpProcess : IAsyncDisposable
  {
    private readonly Process _process;
    private readonly List<string> _stderr = [];
    private int _nextId;

    private McpProcess(Process process) => _process = process;

    public static async Task<McpProcess> StartAsync(string workingDirectory)
    {
      var startInfo = new ProcessStartInfo("dotnet")
      {
        WorkingDirectory = workingDirectory,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      startInfo.ArgumentList.Add(ServerDll());
      startInfo.ArgumentList.Add("mcp");

      var server = new McpProcess(Process.Start(startInfo)!);
      server._process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (server._stderr) server._stderr.Add(e.Data); };
      server._process.BeginErrorReadLine();

      await server.CallAsync("initialize", new JsonObject
      {
        ["protocolVersion"] = "2025-06-18",
        ["capabilities"] = new JsonObject(),
        ["clientInfo"] = new JsonObject { ["name"] = "integration-test", ["version"] = "1.0.0" },
      }, CallTimeout);
      await server.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
      return server;
    }

    public async Task<JsonObject> CallToolAsync(string name, JsonObject arguments, TimeSpan timeout)
    {
      var result = await CallAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments }, timeout);
      var text = result["content"]!.AsArray().Single()!["text"]!.GetValue<string>();
      Assert.False(result["isError"]?.GetValue<bool>() ?? false, $"{name} returned an error: {text}");
      return JsonNode.Parse(text)!.AsObject();
    }

    public async Task<JsonObject> CallAsync(string method, JsonObject parameters, TimeSpan timeout)
    {
      var id = Interlocked.Increment(ref _nextId);
      await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });

      using var cts = new CancellationTokenSource(timeout);
      while (true)
      {
        var line = await _process.StandardOutput.ReadLineAsync(cts.Token)
            ?? throw new InvalidOperationException($"Server exited while waiting for '{method}'. stderr:\n{Stderr()}");

        JsonObject message;
        try
        {
          message = JsonNode.Parse(line)!.AsObject();
        }
        catch (JsonException)
        {
          throw new Xunit.Sdk.XunitException($"Non-protocol output on stdout: {line}");
        }
        Assert.Equal("2.0", message["jsonrpc"]?.GetValue<string>());

        if (message["id"]?.GetValue<int>() != id) continue;
        if (message["error"] is { } error)
          throw new Xunit.Sdk.XunitException($"'{method}' failed: {error.ToJsonString()}");
        return message["result"]!.AsObject();
      }
    }

    public async Task SendAsync(JsonObject message)
    {
      await _process.StandardInput.WriteLineAsync(message.ToJsonString());
      await _process.StandardInput.FlushAsync();
    }

    private string Stderr()
    {
      lock (_stderr) return string.Join("\n", _stderr);
    }

    // Mirrors the DEBUG layout BuildHostFactory relies on to find the BuildServer.
    private static string ServerDll() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EasyDotnet.IDE", "bin", "Debug", "net8.0", "EasyDotnet.IDE.dll"));

    public async ValueTask DisposeAsync()
    {
      _process.StandardInput.Close();
      if (!_process.WaitForExit(TimeSpan.FromSeconds(10)))
      {
        _process.Kill(entireProcessTree: true);
      }
      await _process.WaitForExitAsync();
      _process.Dispose();
    }
  }
}