using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.IDE.OutputWindow;
using Newtonsoft.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Commands;

/// <summary>
/// Command to start the debugger output window / REPL.
/// </summary>
public sealed class OutputWindowCommand : AsyncCommand<OutputWindowCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandArgument(0, "<PIPE_NAME>")]
    [Description("Named pipe to connect to for debugger communication")]
    public string PipeName { get; init; } = "";

    [CommandOption("--timeout <SECONDS>")]
    [Description("Connection timeout in seconds")]
    [DefaultValue(5)]
    public int TimeoutSeconds { get; init; } = 5;
  }

  public override async Task<int> ExecuteAsync(
    CommandContext context,
    Settings settings,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(settings.PipeName))
    {
      AnsiConsole.MarkupLine("[red]Error: Pipe name is required[/]");
      return 1;
    }

    // Initialize components
    var state = new ReplState();
    var controller = new Controller(state);
    var layoutManager = new LayoutManager(state);

    var connection = new PipeConnection();

    try
    {
      // Show connecting message
      AnsiConsole.MarkupLine($"Connecting to debugger via pipe '{settings.PipeName}'...");

      // Connect to named pipe
      var stream = await connection.ConnectAsync(
        settings.PipeName,
        cancellationToken);

      state.DebuggerState = DebuggerState.Running;
      state.AppendOutput("✓ Connected to debugger");

      // Clear the connecting message
      AnsiConsole.Clear();

      var jsonRpc = ServerBuilder.Build(stream, stream);

      jsonRpc.AddLocalRpcTarget(new Controller(state));
      jsonRpc.StartListening();

      var renderLoop = new RenderLoop(state, layoutManager);
      var inputHandler = new InputHandler(state, jsonRpc, layoutManager);

      var renderTask = Task.Run(
        () => renderLoop.StartAsync(cancellationToken),
        cancellationToken);

      await Task.Delay(100, cancellationToken);

      var inputTask = Task.Run(
        () => inputHandler.StartAsync(renderLoop, cancellationToken),
        cancellationToken);

      await jsonRpc.Completion;
      return 0;
    }
    catch (TimeoutException ex)
    {
      AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
      return 1;
    }
    catch (OperationCanceledException)
    {
      AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
      return 0;
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Unexpected error: {ex.Message}[/]");
      AnsiConsole.WriteException(ex);
      return 1;
    }
    finally
    {
      state.DebuggerState = DebuggerState.Disconnected;
    }
  }
}

public static class ServerBuilder
{
  public static JsonRpc Build(Stream writer, Stream reader)
  {
    var formatter = CreateJsonMessageFormatter();
    var handler = new HeaderDelimitedMessageHandler(writer, reader, formatter);
    var jsonRpc = new JsonRpc(handler);

    // jsonRpc.TraceSource.Switch.Level = SourceLevels.Verbose;
    // jsonRpc.TraceSource.Listeners.Add(new ConsoleTraceListener());
    return jsonRpc;
  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };
}