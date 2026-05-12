using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Picker.Models;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.UserSecrets.Services;

public class UserSecretsFlowService(
    UserSecretsService userSecretsService,
    IEditorService editorService,
    IClientService clientService,
    WorkspaceBuildHostManager buildHostManager,
    ILogger<UserSecretsFlowService> logger)
{
  public async Task OpenSecretsAsync(CancellationToken ct)
  {
    try
    {
      var projects = await ResolveRunnableProjectsAsync(ct);

      if (projects.Count == 0)
      {
        await editorService.DisplayError("No runnable projects found");
        return;
      }

      var choices = projects
          .Select(p => new PickerChoice<ValidatedDotnetProject>(
              p.ProjectFullPath,
              p.ProjectName,
              p))
          .ToArray();

      var selected = await editorService.RequestPickerAsync(
          "Select project to edit secrets",
          choices,
          (p, token) => BuildPreviewAsync(p, token),
          ct);

      if (selected is null)
        return;

      var filePath = await userSecretsService.EnsureSecretsAsync(
          selected.ProjectFullPath,
          selected.Raw.UserSecretsId,
          ct);

      await editorService.RequestOpenBuffer(filePath);
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Secrets picker cancelled");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error opening secrets");
      await editorService.DisplayError($"Failed to open secrets: {ex.Message}");
    }
  }

  private async Task<List<ValidatedDotnetProject>> ResolveRunnableProjectsAsync(CancellationToken ct)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    if (solutionFile is not null)
    {
      var projects = await buildHostManager.GetProjectsFromSolutionAsync(solutionFile, p => p.IsRunnable, ct: ct);
      return [.. projects.DistinctBy(p => p.ProjectFullPath)];
    }

    return await buildHostManager.GetProjectsFromDirectoryAsync(
        clientService.RequireRootDir(),
        filter: p => p.IsRunnable,
        ct: ct);
  }

  private Task<PreviewResult> BuildPreviewAsync(ValidatedDotnetProject project, CancellationToken _)
  {
    var secretsId = project.Raw.UserSecretsId;
    if (!string.IsNullOrEmpty(secretsId))
    {
      var path = userSecretsService.GetSecretsPathIfExists(secretsId);
      if (path is not null && File.Exists(path))
        return Task.FromResult<PreviewResult>(new PreviewResult.File(path));
    }

    return Task.FromResult<PreviewResult>(new PreviewResult.Text(
        ["Secrets file does not exist", "<CR> to create"],
        "json"));
  }
}