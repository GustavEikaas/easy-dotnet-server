using System;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.ProjXLanguageServer;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public class ProjXLanguageServerCommand : AsyncCommand<ProjXLanguageServerCommand.Settings>
{
  public sealed class Settings : CommandSettings;

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    await LanguageServer.Start(Console.OpenStandardOutput(), Console.OpenStandardInput(), cancellationToken);
    return 0;
  }
}