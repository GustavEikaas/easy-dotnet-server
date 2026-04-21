using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.Client.Quickfix;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspacePreBuildService(
    IEditorService editorService,
    WorkspaceBuildHostManager buildHostManager)
{
  public async Task<bool> BuildBeforeRunAsync(string path, string name, CancellationToken ct)
  {
    var token = Guid.NewGuid().ToString();

    await editorService.SendProgressStart(token, "Restoring...", $"Restoring {name}");
    List<RestoreResult> restoreResults;
    try
    {
      restoreResults = await buildHostManager.RestoreNugetPackagesAsync(
        new RestoreRequest([path]), ct).ToListAsync(ct);
    }
    finally
    {
      await editorService.SendProgressEnd(token);
    }

    if (!restoreResults.All(r => r.Success))
    {
      var errors = restoreResults.SelectMany(r => r.Output?.Diagnostics is { } d ? MapErrors(d) : []);
      await editorService.SetQuickFixList([.. errors]);
      await editorService.DisplayError($"Restore failed for {name}");
      return false;
    }

    await editorService.SendProgressStart(token, "Building...", $"Building {name}");
    List<BatchBuildResult> buildResults;
    try
    {
      buildResults = await buildHostManager.BatchBuildAsync(
        new BatchBuildRequest([path], "Debug"), ct).ToListAsync(ct);
    }
    finally
    {
      await editorService.SendProgressEnd(token);
    }

    if (buildResults.Any(r => r.Kind == BatchBuildResultKind.Finished && r.Success == false))
    {
      var errors = buildResults.SelectMany(r => r.Output?.Diagnostics is { } d ? MapErrors(d) : []);
      await editorService.SetQuickFixList([.. errors]);
      await editorService.DisplayError($"Build failed for {name}");
      return false;
    }

    return true;
  }

  private static IEnumerable<QuickFixItem> MapErrors(BuildDiagnostic[] diagnostics) =>
    diagnostics
      .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
      .Select(d => new QuickFixItem(
        FileName: d.File ?? "",
        LineNumber: d.LineNumber,
        ColumnNumber: d.ColumnNumber,
        Text: string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
        Type: QuickFixItemType.Error));
}