using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Upgrade.Models;
using EasyDotnet.IDE.Upgrade.Service;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Upgrade.Controllers;

public sealed class UpgradeWizardController(
    IClientService clientService,
    UpgradeAnalysisService analysisService,
    UpgradeApplyService applyService,
    ChangelogService changelogService,
    NugetService nugetService) : BaseController
{
  [JsonRpcMethod("nuget/upgradeWizard/open", UseSingleObjectParameterDeserialization = true)]
  public async Task OpenAsync(OpenRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await analysisService.AnalyzeAsync(request.TargetPath, ct);
  }

  [JsonRpcMethod("nuget/upgradeWizard/apply", UseSingleObjectParameterDeserialization = true)]
  public async Task ApplyAsync(ApplyRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await applyService.ApplyAsync(request, ct);
    // Re-analyze so the client list reflects the new state after upgrades.
    await analysisService.AnalyzeAsync(request.TargetPath, ct);
  }

  [JsonRpcMethod("nuget/upgradeWizard/cancel")]
  public Task CancelAsync(CancellationToken _)
  {
    applyService.Cancel();
    return Task.CompletedTask;
  }

  [JsonRpcMethod("nuget/upgradeWizard/changelog", UseSingleObjectParameterDeserialization = true)]
  public Task<ChangelogResult> GetChangelogAsync(ChangelogRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    return changelogService.GetChangelogAsync(request.PackageId, request.Version, ct);
  }

  [JsonRpcMethod("nuget/upgradeWizard/versions", UseSingleObjectParameterDeserialization = true)]
  public async Task<string[]> GetVersionsAsync(VersionsRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    var versions = await nugetService.GetPackageVersionsAsync(request.PackageId, ct);
    return versions.Select(v => v.ToNormalizedString()).ToArray();
  }
}