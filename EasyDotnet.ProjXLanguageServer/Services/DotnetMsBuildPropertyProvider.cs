using System.Diagnostics;
using System.Text.Json;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IProjXMsBuildPropertyProvider
{
  IReadOnlyDictionary<string, string?> GetProperties(string projectPath, params string[] propertyNames);
}

public sealed class DotnetMsBuildPropertyProvider : IProjXMsBuildPropertyProvider
{
  private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

  public IReadOnlyDictionary<string, string?> GetProperties(string projectPath, params string[] propertyNames)
  {
    if (propertyNames.Length == 0)
    {
      return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    var startInfo = new ProcessStartInfo("dotnet")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    startInfo.ArgumentList.Add("msbuild");
    startInfo.ArgumentList.Add(projectPath);
    foreach (var propertyName in propertyNames)
    {
      startInfo.ArgumentList.Add($"-getProperty:{propertyName}");
    }
    startInfo.ArgumentList.Add("-nologo");
    startInfo.ArgumentList.Add("-v:quiet");

    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"dotnet msbuild property query failed: {stderr}");
    }

    var response = JsonSerializer.Deserialize<MsBuildGetPropertyResponse>(stdout, JsonOptions);
    return response?.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
  }

  private sealed record MsBuildGetPropertyResponse(Dictionary<string, string?> Properties);
}
