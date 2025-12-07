using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Debugger;
using EasyDotnet.IDE.OutputWindow;
using Newtonsoft.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;
using StreamJsonRpc;
namespace EasyDotnet.IDE.Commands;

/// <summary>
/// Command to start the debugger output window
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

    var connection = new PipeConnection();
    try
    {
      AnsiConsole.MarkupLine($"Connecting to debugger via pipe '{settings.PipeName}'...");

      var stream = await connection.ConnectAsync(settings.PipeName, cancellationToken);

      AnsiConsole.Clear();

      AnsiConsole.MarkupLine("[green]✓ Connected to debugger[/]");
      AnsiConsole.WriteLine();

      var jsonRpc = ServerBuilder.Build(stream, stream);

      jsonRpc.AddLocalRpcMethod("debugger/output", (DebugOutputEvent outputEvent) => FormatAndDisplayOutput(outputEvent));

      jsonRpc.Disconnected += (sender, args) => AnsiConsole.MarkupLine("[yellow]Debugger disconnected[/]");

      jsonRpc.StartListening();

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
  }

  private static void FormatAndDisplayOutput(DebugOutputEvent outputEvent)
  {
    if (outputEvent.Source != null)
    {
      return;
    }

    foreach (var line in outputEvent.Output)
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        AnsiConsole.WriteLine();
        continue;
      }

      var color = DetectColorFromLine(line);
      AnsiConsole.Markup($"[{color}]{Markup.Escape(line)}[/]");
      AnsiConsole.WriteLine();
    }
  }

  private static string DetectColorFromLine(string line)
  {
    var lowerLine = line.ToLowerInvariant();

    if (lowerLine.Contains(": trace:"))
      return "grey dim";

    if (lowerLine.Contains(": debug:"))
      return "grey";

    if (lowerLine.Contains(": information:"))
      return "white";

    if (lowerLine.Contains(": warning:"))
      return "yellow";

    if (lowerLine.Contains(": error:"))
      return "red";

    if (lowerLine.Contains(": critical:"))
      return "red bold";

    if (lowerLine.TrimStart().StartsWith("at ") ||
        line.StartsWith("   at ") ||
        (lowerLine.StartsWith("system.") && lowerLine.Contains("exception")))
    {
      return "red dim";
    }

    return "white";
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