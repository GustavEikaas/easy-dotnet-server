using EasyDotnet.IDE.Interfaces;

namespace EasyDotnet.IDE.UserSecrets.Services;

public class UserSecretsService(IProcessQueue processQueue)
{
  private readonly string _basePath = OperatingSystem.IsWindows()
      ? Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "Microsoft",
          "UserSecrets")
      : Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
          ".microsoft",
          "usersecrets");

  /// <summary>
  /// Ensures the project has a UserSecretsId and returns the path to its secrets.json.
  /// If <paramref name="knownSecretsId"/> is provided it is used directly; otherwise
  /// <c>dotnet user-secrets init</c> is run to generate and persist one.
  /// </summary>
  public async Task<string> EnsureSecretsAsync(
      string projectPath,
      string? knownSecretsId,
      CancellationToken ct = default)
  {
    if (!File.Exists(projectPath))
      throw new FileNotFoundException("Project file not found", projectPath);

    if (!string.IsNullOrEmpty(knownSecretsId))
      return GetSecretsPath(knownSecretsId);

    var newId = Guid.NewGuid().ToString();
    var (success, _, _) = await processQueue.RunProcessAsync(
        "dotnet",
        $"user-secrets init --project \"{projectPath}\" --id {newId}",
        new ProcessOptions(true));

    if (!success)
      throw new InvalidOperationException("Failed to initialize user secrets");

    EnsureSecretsDirectory(newId);
    var filePath = GetSecretsPath(newId);

    if (!File.Exists(filePath))
      File.WriteAllText(filePath, "{ }");

    return filePath;
  }

  public string? GetSecretsPathIfExists(string secretsId) =>
      string.IsNullOrEmpty(secretsId) ? null : GetSecretsPath(secretsId);

  private void EnsureSecretsDirectory(string id)
  {
    var dir = Path.Combine(_basePath, id);
    if (!Directory.Exists(dir))
      Directory.CreateDirectory(dir);
  }

  private string GetSecretsPath(string id) =>
      Path.Combine(_basePath, id, "secrets.json");
}
