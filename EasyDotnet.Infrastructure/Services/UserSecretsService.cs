using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Secrets;

namespace EasyDotnet.Infrastructure.Services;

public class UserSecretsService(IMsBuildService msBuildService, IProcessQueue processQueue) : IUserSecretsService
{
  private readonly string _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets");

  public async Task<ProjectUserSecret> AddUserSecretsId(string projectPath)
  {
    if (!File.Exists(projectPath))
    {
      throw new FileNotFoundException("Project file not found", projectPath);
    }

    var project = await msBuildService.GetOrSetProjectPropertiesAsync(projectPath);

    var currentSecretsId = project.UserSecretsId;

    if (!string.IsNullOrEmpty(currentSecretsId))
    {
      var path = GetSecretsPath(currentSecretsId);
      return new(currentSecretsId, path);
    }

    var newSecretsId = Guid.NewGuid().ToString();

    var (success, _, _) = await processQueue.RunProcessAsync("dotnet", $"user-secrets init --project {projectPath} --id {newSecretsId}", new ProcessOptions(true));
    if (!success)
    {
      throw new Exception("Failed to initialize user secrets");
    }

    await msBuildService.InvalidateProjectProperties(projectPath);

    EnsureSecretsDirectory(newSecretsId);
    var secretsFilePath = GetSecretsPath(newSecretsId);

    if (!File.Exists(secretsFilePath))
    {
      File.WriteAllText(secretsFilePath, "{ }");
    }
    return new(newSecretsId, secretsFilePath);
  }

  private void EnsureSecretsDirectory(string id)
  {
    var secretsDir = Path.Combine(_basePath, id);
    if (!Directory.Exists(secretsDir))
    {
      Directory.CreateDirectory(secretsDir);
    }
  }

  private string GetSecretsPath(string id)
  {
    var secretsDir = Path.Combine(_basePath, id);
    var secretsFilePath = Path.Combine(secretsDir, "secrets.json");
    return secretsFilePath;
  }
}