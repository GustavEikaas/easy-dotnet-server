using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class CompatCommand : Command
{
  public override int Execute(CommandContext context, CancellationToken cancellationToken)
  {
    AnsiConsole.MarkupLine("[yellow]Usage:[/] dotnet easydotnet compat <run|build|test>");
    return 1;
  }
}