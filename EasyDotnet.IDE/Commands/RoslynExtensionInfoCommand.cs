using System.Text.Json;
using EasyDotnet.IDE.Services;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class RoslynExtensionInfoCommand : Command
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public override int Execute(CommandContext context, CancellationToken cancellationToken)
  {
    var info = new RoslynExtensionInfo(RoslynLocator.GetEasyDotnetRoslynLanguageServicesPath(), RoslynLocator.GetExternalAccessExtensionsPath());
    Console.WriteLine(JsonSerializer.Serialize(info, JsonOptions));
    return 0;
  }

  private sealed record RoslynExtensionInfo(string EasyDotnetRoslynLanguageServicesPath, string? DevKitDependencyPath);
}
