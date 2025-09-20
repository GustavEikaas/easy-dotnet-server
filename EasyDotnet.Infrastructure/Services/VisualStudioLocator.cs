using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace EasyDotnet.Infrastructure.Services;

public class VisualStudioLocator(IMemoryCache cache, IClientService clientService, IProcessQueue processQueue) : IVisualStudioLocator
{
  public async Task<string> GetVisualStudioMSBuildPath()
  {
    var vsCommand = await cache.GetOrCreateAsync("MSBuildInfo", async entry =>
    {
      var result = await GetVisualStudioMSBuild();
      return string.IsNullOrWhiteSpace(result) ? throw new InvalidOperationException("Could not locate MSBuild on this machine.") : result;
    });

    return vsCommand ?? throw new InvalidOperationException("Could not locate MSBuild on this machine.");
  }

  public string? GetApplicationHostConfig() => cache.GetOrCreate("ApplicationHostConfig", entry =>
                                                {
                                                  var sln = clientService.ProjectInfo?.SolutionFile;
                                                  if (string.IsNullOrEmpty(sln))
                                                  {
                                                    return null;
                                                  }

                                                  var slnDir = Path.GetDirectoryName(sln);
                                                  if (string.IsNullOrEmpty(slnDir))
                                                  {
                                                    return null;
                                                  }

                                                  var slnName = Path.GetFileNameWithoutExtension(sln);

                                                  var configPath = Path.Combine(slnDir, ".vs", slnName, "config", "applicationhost.config");

                                                  return File.Exists(configPath) ? configPath : null;
                                                });

  public string? GetIisExpressExe() => cache.GetOrCreate("IisExpressExe", entry => new[]
  {
  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IIS Express", "iisexpress.exe"),
  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "IIS Express", "iisexpress.exe")
}.FirstOrDefault(File.Exists));

  private async Task<string?> GetVisualStudioMSBuild()
  {
    try
    {
      var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
      var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

      if (!File.Exists(vswhere))
      {
        return null;
      }

      var (success, stdout, stderr) = await processQueue.RunProcessAsync(vswhere, "-latest -property installationPath", new ProcessOptions(true));
      if (!success)
      {
        var message = $"Failed to find Visual Studio installation path.\nStdOut: {stdout}\nStdErr: {stderr}";
        throw new InvalidOperationException(message);
      }

      var basePath = stdout.Trim(Environment.NewLine.ToCharArray());

      if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
      {
        return null;
      }

      var msbuildPath = Path.Combine(basePath, "MSBuild", "Current", "Bin", "MSBuild.exe");
      return File.Exists(msbuildPath) ? msbuildPath : null;
    }
    catch
    {
      return null;
    }
  }
}