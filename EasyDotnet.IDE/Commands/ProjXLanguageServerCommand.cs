using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public class ProjXLanguageServerCommand : AsyncCommand<ProjXLanguageServerCommand.Settings>
{
  public sealed class Settings : CommandSettings;

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var rpc = EasyDotnet.ProjXLanguageServer.JsonRpcServerBuilder.Build(
        Console.OpenStandardOutput(),
        Console.OpenStandardInput(),
        buildServiceProvider: EasyDotnet.DiModules.BuildServiceProvider,
        logLevel: SourceLevels.Off);
    rpc.StartListening();
    await rpc.Completion;
    return 0;
  }
}
