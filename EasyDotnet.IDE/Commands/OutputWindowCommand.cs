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
    AvaloniaOutputWindow.Run(settings.PipeName);
    return 0;
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