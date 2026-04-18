using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Controllers.Nuget;
using EasyDotnet.IDE.Editor;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Quickfix;
using EasyDotnet.IDE.Models.Solution;
using EasyDotnet.IDE.Picker.Models;
using EasyDotnet.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.PackageManager;

public class PackageManagerService(
    NugetService nugetService,
    IEditorService editorService,
    IClientService clientService,
    ISolutionService solutionService,
    IBuildHostManager buildHostManager,
    IProcessQueue processQueue,
    ILogger<PackageManagerService> logger)
{
  private const int ProjectSearchDepth = 3;

  public async Task AddPackageAsync(AddPackageRequest request, CancellationToken ct)
  {
    using var progress = new ProgressScope(editorService, "Add Package", "Searching NuGet…");

    var packages = await editorService.RequestMultiLivePickerAsync(
        "Search NuGet packages (multi-select)",
        (query, token) => SearchPackagesAsync(query, request.IncludePrerelease, token),
        (pkg, token) => BuildPreviewAsync(pkg, token),
        ct);

    if (packages is null or { Length: 0 })
    {
      return;
    }

    var selections = new List<(NugetPackageMetadata Package, string Version)>();
    foreach (var package in packages)
    {
      progress.Report($"Fetching versions for {package.Id}…");

      var versions = (await nugetService.GetPackageVersionsAsync(
          package.Id,
          ct,
          includePrerelease: request.IncludePrerelease))
          .Select(v => new PickerChoice<string>(v.ToNormalizedString(), v.ToNormalizedString(), v.ToNormalizedString()))
          .ToArray();

      if (versions.Length == 0)
      {
        await editorService.DisplayError($"No versions found for {package.Id}");
        return;
      }

      var version = await editorService.RequestPickerAsync(
          $"Pick version for {package.Id}",
          versions,
          ct: ct);

      if (version is null)
      {
        return;
      }

      selections.Add((package, version));
    }

    var projectPath = request.ProjectPath ?? await PickProjectAsync(ct);
    if (projectPath is null)
    {
      return;
    }

    foreach (var (package, version) in selections)
    {
      progress.Report($"Adding {package.Id}@{version}…");

      var (addSuccess, _, addErr) = await processQueue.RunProcessAsync(
          "dotnet",
          $"add \"{projectPath}\" package \"{package.Id}\" --version \"{version}\"",
          new ProcessOptions(KillOnTimeout: true),
          ct);

      if (!addSuccess)
      {
        logger.LogError("dotnet add package failed for {pkg}: {err}", package.Id, addErr);
        await editorService.DisplayError($"Failed to add {package.Id}: {addErr}");
        return;
      }
    }

    progress.Report("Restoring packages…");

    var restoreResults = await buildHostManager
        .RestoreNugetPackagesAsync(new RestoreRequest([projectPath]), ct)
        .ToListAsync(ct);

    var allDiagnostics = restoreResults
        .Where(r => r.Output is not null)
        .SelectMany(r => r.Output!.Diagnostics)
        .ToList();

    var errors = allDiagnostics
        .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
        .Select(d => new QuickFixItem(
            d.File ?? "",
            d.LineNumber,
            d.ColumnNumber,
            string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
            QuickFixItemType.Error))
        .ToList();

    var warnings = allDiagnostics
        .Where(d => d.Severity == BuildDiagnosticSeverity.Warning)
        .Select(d => new QuickFixItem(
            d.File ?? "",
            d.LineNumber,
            d.ColumnNumber,
            string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
            QuickFixItemType.Warning))
        .ToList();

    if (errors.Count > 0)
    {
      await editorService.SetQuickFixList([.. errors.Concat(warnings)]);
      return;
    }

    if (warnings.Count > 0)
    {
      await editorService.SetQuickFixListSilent([.. warnings]);
    }

    var names = string.Join(", ", selections.Select(s => $"{s.Package.Id}@{s.Version}"));
    await editorService.DisplayMessage($"Added successfully: {names}");
  }

  private async Task<PickerChoice<NugetPackageMetadata>[]> SearchPackagesAsync(
      string query,
      bool includePrerelease,
      CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      return [];
    }

    var results = await nugetService.SearchAllSourcesByNameAsync(
        query, ct, take: 20, includePrerelease: includePrerelease);

    return [.. results
        .SelectMany(kvp => kvp.Value.Select(m => NugetPackageMetadata.From(m, kvp.Key)))
        .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .Select(m => new PickerChoice<NugetPackageMetadata>(
            $"{m.Id}:{m.Source}",
            m.Id,
            m))];
  }

  private static Task<PreviewResult> BuildPreviewAsync(NugetPackageMetadata pkg, CancellationToken _)
  {
    var lines = new List<string>
    {
      $"# {pkg.Id}",
      "",
      $"**Version** {pkg.Version}",
    };

    if (!string.IsNullOrWhiteSpace(pkg.Authors))
      lines.Add($"**Authors** {pkg.Authors}");

    if (pkg.DownloadCount.HasValue)
      lines.Add($"**Downloads** {pkg.DownloadCount.Value:N0}");

    if (pkg.ProjectUrl is not null)
      lines.Add($"**URL** {pkg.ProjectUrl}");

    if (!string.IsNullOrWhiteSpace(pkg.Description))
    {
      lines.Add("");
      lines.Add("---");
      lines.Add("");
      lines.AddRange(WrapText(pkg.Description, 72));
    }

    if (pkg.Tags is { Count: > 0 })
    {
      lines.Add("");
      lines.Add($"> {string.Join(" · ", pkg.Tags)}");
    }

    return Task.FromResult<PreviewResult>(new PreviewResult.Text([.. lines], Filetype: "markdown"));
  }

  private async Task<string?> PickProjectAsync(CancellationToken ct)
  {
    var rootDir = clientService.RequireRootDir();
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    List<string> projectPaths;

    if (solutionFile is not null)
    {
      var solutionProjects = await solutionService.GetProjectsFromSolutionFile(solutionFile, ct);
      projectPaths = [.. solutionProjects.OnlyDotnetProjects().Select(p => p.AbsolutePath)];
    }
    else
    {
      projectPaths = [.. Directory
          .EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
          {
            MaxRecursionDepth = ProjectSearchDepth,
            RecurseSubdirectories = true,
          })];
    }

    if (projectPaths.Count == 0)
    {
      await editorService.DisplayError("No .NET projects found");
      return null;
    }

    if (projectPaths.Count == 1)
    {
      return projectPaths[0];
    }

    var choices = projectPaths
        .Select(p => new PickerChoice<string>(p, Path.GetFileNameWithoutExtension(p), p))
        .ToArray();

    return await editorService.RequestPickerAsync("Pick project to add package to", choices, ct: ct);
  }

  private static IEnumerable<string> WrapText(string text, int maxWidth)
  {
    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var line = new System.Text.StringBuilder();
    foreach (var word in words)
    {
      if (line.Length > 0 && line.Length + 1 + word.Length > maxWidth)
      {
        yield return line.ToString();
        line.Clear();
      }
      if (line.Length > 0)
      {
        line.Append(' ');
      }

      line.Append(word);
    }
    if (line.Length > 0)
    {
      yield return line.ToString();
    }
  }
}