using EasyDotnet.IDE.Upgrade.Models;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Upgrade.Dispatch;

/// <summary>
/// Single choke point for all outbound upgrade wizard notifications to the Lua client.
/// </summary>
public sealed class UpgradeDispatcher(JsonRpc rpc)
{
  public Task SendInitializedAsync(UpgradeCandidate[] candidates) =>
      rpc.NotifyWithParameterObjectAsync("upgradeWizard/initialized", new { candidates });

  public Task SendStatusAsync(UpgradeWizardStatus status) =>
      rpc.NotifyWithParameterObjectAsync("upgradeWizard/status", status);

  public Task SendProgressAsync(UpgradeProgress progress) =>
      rpc.NotifyWithParameterObjectAsync("upgradeWizard/progress", progress);

  public Task SendResultAsync(UpgradeResult result) =>
      rpc.NotifyWithParameterObjectAsync("upgradeWizard/result", result);
}