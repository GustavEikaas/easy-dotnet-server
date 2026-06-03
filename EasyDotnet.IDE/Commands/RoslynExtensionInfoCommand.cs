using System.Text.Json;
using EasyDotnet.IDE.Services;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class RoslynExtensionInfoCommand : Command
{
  public override int Execute(CommandContext context, CancellationToken cancellationToken)
  {
    var info = new RoslynExtensionInfo(RoslynLocator.GetEasyDotnetRoslynLanguageServicesPath(), RoslynLocator.GetExternalAccessExtensionsPath());
    Console.WriteLine(JsonSerializer.Serialize(info), JsonSerializerDefaults.Web);
    return 0;
  }

  private sealed record RoslynExtensionInfo(string EasyDotnetRoslynLanguageServicesPath, string? DevKitDependencyPath);
}