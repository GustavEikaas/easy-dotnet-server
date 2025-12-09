using System;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.ProjectLanguageServer;
using Spectre.Console.Cli;
namespace EasyDotnet.IDE.Commands;

public class ProjectLanguageServerCommand : AsyncCommand<ProjectLanguageServerCommand.Settings>
{
  public sealed class Settings : CommandSettings;

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    await LanguageServer.Start(Console.OpenStandardOutput(), Console.OpenStandardInput(), cancellationToken);
    return 0;
  }
}

