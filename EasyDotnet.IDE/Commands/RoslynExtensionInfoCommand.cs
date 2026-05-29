using System.Text.Json;
using EasyDotnet.IDE.Services;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class RoslynExtensionInfoCommand : Command
{
  public override int Execute(CommandContext context, CancellationToken cancellationToken)
  {
    var info = new RoslynExtensionInfo(
        RoslynLocator.GetEasyDotnetRoslynLanguageServicesPath());

    Console.WriteLine(JsonSerializer.Serialize(info));
    return 0;
  }

  private sealed record RoslynExtensionInfo(string EasyDotnetRoslynLanguageServicesPath);
}