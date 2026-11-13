using System.Diagnostics;
using System.Reflection;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Mcp.Headless;
using EasyDotnet.IDE.Mcp.Tools;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.TestRunner.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EasyDotnet.IDE.Mcp;

/// <summary>
/// Runs the core services behind an MCP server on stdin/stdout, for AI agents. There is no
/// editor: prompts fail, notifications are dropped and every log line goes to stderr.
/// </summary>
public static class McpServerHost
{
  public const string Instructions = """
    Builds and tests .NET code through easy-dotnet's long-lived build server and test runner.
    Prefer these tools over `dotnet build` and `dotnet test`: results come back as compact JSON
    (deduplicated build errors, failing tests with the failing line in user code), tests are
    selected by id, file and line, or name without framework-specific filter syntax, and MTP and
    VSTest projects run in one call. The first test call discovers the solution's tests and can
    take a while; later calls reuse that state.
    """;

  public static ServiceProvider BuildServiceProvider(SourceLevels logLevel)
  {
    var services = new ServiceCollection();
    DiModules.AddCoreServices(services, logLevel, logToStandardError: true);

    services.AddSingleton<IEditorService, HeadlessEditorService>();
    services.AddSingleton<INotificationService, NullNotificationService>();
    services.AddSingleton<McpTestRunnerClient>();
    services.AddSingleton<ITestRunnerClient>(sp => sp.GetRequiredService<McpTestRunnerClient>());
    services.AddSingleton<McpWorkspace>();
    services.AddSingleton<TestSelection>();

    services
        .AddMcpServer(options =>
        {
          options.ServerInfo = new Implementation
          {
            Name = "easy-dotnet",
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
          };
          options.ServerInstructions = Instructions;
        })
        .WithStdioServerTransport()
        .WithTools<BuildTools>()
        .WithTools<TestTools>();

    return services.BuildServiceProvider();
  }

  public static async Task RunAsync(string rootDir, string? solutionFile, SourceLevels logLevel, CancellationToken ct)
  {
    await using var serviceProvider = BuildServiceProvider(logLevel);

    serviceProvider.GetRequiredService<ClientInitializer>().Initialize(
        new ProjectInfo(rootDir, solutionFile),
        new ClientInfo("mcp", Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0), Environment.ProcessId),
        new ClientOptions());

    await serviceProvider.GetRequiredService<McpServer>().RunAsync(ct);
  }
}