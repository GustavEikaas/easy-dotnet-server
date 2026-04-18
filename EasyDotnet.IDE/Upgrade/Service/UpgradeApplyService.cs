using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Upgrade.Dispatch;
using EasyDotnet.IDE.Upgrade.Models;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Upgrade.Service;

public sealed class UpgradeApplyService(
    UpgradeDispatcher dispatcher,
    IProcessQueue processQueue,
    IBuildHostManager buildHostManager,
    ILogger<UpgradeApplyService> logger)
{
  private CancellationTokenSource? _applyCts;

  public void Cancel() => _applyCts?.Cancel();

  public async Task ApplyAsync(ApplyRequest request, CancellationToken ct)
  {
    _applyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var linkedCt = _applyCts.Token;

    await dispatcher.SendStatusAsync(new UpgradeWizardStatus("Applying", $"Updating {request.Selections.Length} package(s)…"));

    var updated = new List<UpgradeResultItem>();
    var failed  = new List<UpgradeResultItem>();

    for (int i = 0; i < request.Selections.Length; i++)
    {
      if (linkedCt.IsCancellationRequested) break;

      var sel = request.Selections[i];
      await dispatcher.SendProgressAsync(new UpgradeProgress(
          PackageId: sel.PackageId,
          Current: i + 1,
          Total: request.Selections.Length,
          Success: true));

      var projectsToUpdate = sel.AffectedProjects is { Length: > 0 }
          ? sel.IsCentrallyManaged
              ? [sel.AffectedProjects[0]]
              : sel.AffectedProjects
          : [request.TargetPath];

      // ── Step 1: dotnet add --no-restore ──────────────────────────────────
      string? addError = null;
      foreach (var proj in projectsToUpdate)
      {
        if (linkedCt.IsCancellationRequested) break;

        var (ok, _, stderr) = await processQueue.RunProcessAsync(
            "dotnet",
            $"add \"{proj}\" package \"{sel.PackageId}\" --version \"{sel.TargetVersion}\" --no-restore",
            new ProcessOptions(KillOnTimeout: true),
            linkedCt);

        if (!ok) { addError = stderr?.Trim(); break; }
      }

      if (addError is not null)
      {
        logger.LogError("dotnet add failed for {pkg}: {err}", sel.PackageId, addError);
        failed.Add(new UpgradeResultItem(sel.PackageId, sel.CurrentVersion, sel.TargetVersion, addError));
        await dispatcher.SendProgressAsync(new UpgradeProgress(sel.PackageId, i + 1, request.Selections.Length, false, addError));
        continue;
      }

      // ── Step 2: restore to validate the upgrade compiles ─────────────────
      await dispatcher.SendStatusAsync(new UpgradeWizardStatus("Applying", $"Restoring {sel.PackageId}…"));

      var restoreProjects = (sel.AffectedProjects is { Length: > 0 } ? sel.AffectedProjects : [request.TargetPath])
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToArray();

      var restoreResults = await buildHostManager
          .RestoreNugetPackagesAsync(new RestoreRequest(restoreProjects), linkedCt)
          .ToListAsync(linkedCt);

      var restoreError = restoreResults
          .Where(r => !r.Success)
          .Select(r =>
          {
            // Prefer structured diagnostics — they carry the actual NuGet conflict messages.
            var errors = r.Output?.Diagnostics
                .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
                .Select(d => string.IsNullOrWhiteSpace(d.Code)
                    ? d.Message
                    : $"{d.Code}: {d.Message}")
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToArray();

            if (errors is { Length: > 0 })
              return string.Join("\n", errors);

            return r.ErrorMessage?.Trim();
          })
          .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

      if (restoreError is not null)
      {
        // ── Step 3 (on failure): revert the add so the project stays clean ──
        logger.LogWarning("Restore failed for {pkg}, reverting: {err}", sel.PackageId, restoreError);

        foreach (var proj in projectsToUpdate)
        {
          await processQueue.RunProcessAsync(
              "dotnet",
              $"add \"{proj}\" package \"{sel.PackageId}\" --version \"{sel.CurrentVersion}\" --no-restore",
              new ProcessOptions(KillOnTimeout: true),
              CancellationToken.None);   // don't cancel the revert
        }

        failed.Add(new UpgradeResultItem(sel.PackageId, sel.CurrentVersion, sel.TargetVersion, restoreError));
        await dispatcher.SendProgressAsync(new UpgradeProgress(sel.PackageId, i + 1, request.Selections.Length, false, restoreError));
      }
      else
      {
        updated.Add(new UpgradeResultItem(sel.PackageId, sel.CurrentVersion, sel.TargetVersion));
        await dispatcher.SendProgressAsync(new UpgradeProgress(sel.PackageId, i + 1, request.Selections.Length, true));
      }
    }

    await dispatcher.SendResultAsync(new UpgradeResult(Updated: [.. updated], Failed: [.. failed]));
    await dispatcher.SendStatusAsync(new UpgradeWizardStatus(
        linkedCt.IsCancellationRequested ? "Failed" : "Done"));
  }
}
