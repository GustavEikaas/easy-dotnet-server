using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class GenerateRpcDocsCommand : AsyncCommand<GenerateRpcDocsCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandOption("--format <FORMAT>")]
    [Description("Output format: 'json' (default) or 'markdown'.")]
    [DefaultValue("json")]
    public string Format { get; init; } = "json";
  }

  public override ValidationResult Validate(CommandContext context, Settings settings)
  {
    var fmt = settings.Format.ToLowerInvariant();
    return fmt is not ("json" or "markdown")
      ? ValidationResult.Error("Invalid format. Must be 'json' or 'markdown'.")
      : ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context,
      Settings settings,
      CancellationToken cancellationToken)
  {
    var format = settings.Format.ToLowerInvariant();

    var (outputPath, content) = format switch
    {
      "json" => ("./rpcDoc.json", RpcDocGenerator.GenerateJsonDoc()),
      "markdown" => ("./rpcDoc.md",
          RpcDocGenerator.GenerateMarkdownDoc().ReplaceLineEndings("\n")),
      _ => throw new ArgumentException("Invalid format. Must be 'json' or 'markdown'.")
    };

    await File.WriteAllTextAsync(outputPath, content, cancellationToken);

    AnsiConsole.MarkupLine(
        $"[green]✔ RPC documentation written to[/] [yellow]{Path.GetFullPath(outputPath)}[/]");

    return 0;
  }

}