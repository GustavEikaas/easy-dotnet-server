using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Caching.Memory;

namespace EasyDotnet.Services;

public enum MSBuildType
{
  SDK,
  VisualStudio
}

public record MSBuildInfo(MSBuildType Type, string Command);

public class VisualStudioLocator(IMemoryCache cache, ClientService clientService)
{
  public string GetVisualStudioMSBuildPath() => cache.GetOrCreate("MSBuildInfo", entry =>
                                      {
                                        var vsCommand = GetVisualStudioMSBuild();
                                        return !string.IsNullOrEmpty(vsCommand) ? vsCommand
                                           : throw new InvalidOperationException("Could not locate MSBuild on this machine.");
                                      }) ?? throw new InvalidOperationException("Could not locate MSBuild on this machine.");

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

  private static string? GetVisualStudioMSBuild()
  {
    try
    {
      var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
      var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

      if (!File.Exists(vswhere))
        return null;

      var process = Process.Start(new ProcessStartInfo
      {
        FileName = vswhere,
        Arguments = "-latest -products * -requires Microsoft.Component.MSBuild -property installationPath",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      var output = process?.StandardOutput.ReadToEnd()?.Trim();
      process?.WaitForExit();

      if (string.IsNullOrEmpty(output) || !Directory.Exists(output))
        return null;

      var msbuildPath = Path.Combine(output, "MSBuild", "Current", "Bin", "MSBuild.exe");
      return File.Exists(msbuildPath) ? msbuildPath : null;
    }
    catch
    {
      return null;
    }
  }
}