using System.IO.Abstractions;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IUserSecretsResolver
{
  string GetSecretsPath(string userSecretsId);
  string EnsureSecretsFile(string userSecretsId);
}

public class UserSecretsResolver(IFileSystem fileSystem) : IUserSecretsResolver
{
  private readonly string _basePath = fileSystem.Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Microsoft",
      "UserSecrets");

  public string GetSecretsPath(string userSecretsId) =>
      fileSystem.Path.Combine(_basePath, userSecretsId, "secrets.json");

  public string EnsureSecretsFile(string userSecretsId)
  {
    var dir = fileSystem.Path.Combine(_basePath, userSecretsId);
    if (!fileSystem.Directory.Exists(dir))
      fileSystem.Directory.CreateDirectory(dir);

    var path = fileSystem.Path.Combine(dir, "secrets.json");
    if (!fileSystem.File.Exists(path))
      fileSystem.File.WriteAllText(path, "{}");

    return path;
  }
}
