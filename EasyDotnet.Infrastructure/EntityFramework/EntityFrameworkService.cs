using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EasyDotnet.Infrastructure.EntityFramework;

public class EntityFrameworkService
{
  private readonly JsonSerializerOptions _jsonSerializerOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private static async Task<EfCommandResult> RunEfCommandAsync(
    List<string> arguments,
    string workingDirectory,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet-ef",
      Arguments = string.Join(" ", arguments),
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = new System.Diagnostics.Process { StartInfo = startInfo };

    var outputBuilder = new StringBuilder();
    var errorBuilder = new StringBuilder();

    process.OutputDataReceived += (sender, e) =>
    {
      if (e.Data != null)
      {
        outputBuilder.AppendLine(e.Data);
      }
    };

    process.ErrorDataReceived += (sender, e) =>
    {
      if (e.Data != null)
      {
        errorBuilder.AppendLine(e.Data);
      }
    };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    await process.WaitForExitAsync(cancellationToken);

    var stdout = outputBuilder.ToString();
    var stderr = errorBuilder.ToString();

    var parsed = EfToolOutputParser.Parse(stdout);

    return new EfCommandResult(
      ExitCode: process.ExitCode,
      Success: process.ExitCode == 0 && parsed.Success,
      JsonData: parsed.JsonData,
      ErrorMessage: parsed.ErrorMessage,
      InfoMessages: parsed.InfoMessages,
      ErrorMessages: parsed.ErrorMessages,
      StandardOutput: stdout,
      StandardError: stderr
    );
  }

  public async Task<List<DbContextInfo>> ListDbContextsAsync(
    string efProjectPath,
    string startupProjectPath,
    string workingDirectory = ".",
    CancellationToken cancellationToken = default)
  {
    var args = new List<string>
  {
    "dbcontext",
    "list",
    "--project", $"\"{efProjectPath}\"",
    "--startup-project", $"\"{startupProjectPath}\"",
    "--json",
    "--prefix-output"
  };

    var result = await RunEfCommandAsync(args, workingDirectory, cancellationToken);

    if (!result.Success)
    {
      var message = string.Join('\n', result.ErrorMessages);
      throw new Exception(message);
    }

    if (string.IsNullOrWhiteSpace(result.JsonData)) return [];

    try
    {
      var contexts = JsonSerializer.Deserialize<List<DbContextInfo>>(
          result.JsonData,
          _jsonSerializerOptions);

      return contexts ?? [];
    }
    catch (JsonException)
    {
      throw new Exception($"Failed to deserialize {result.JsonData}");
    }
  }

}