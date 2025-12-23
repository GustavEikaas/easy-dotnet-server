using Dunet;
using EasyDotnet.MsBuild;

namespace EasyDotnet.Domain.Models.Workspace;

[Union]
public partial record ProjectEntry
{
  public partial record Loaded(DotnetProject Project);
  public partial record Unloaded(string Path);
  public partial record Errored(string Path, string ErrorMessage);
}

public static class ProjectEntryExtensions
{
  public static bool IsRunnable(this ProjectEntry entry) =>
      entry.Match(
          loaded => loaded.Project.IsRunnable(),
          unloaded => true,
          errored => false
      );

  public static string GetPath(this ProjectEntry entry) => entry.Match(
          loaded => loaded.Project.MSBuildProjectFullPath!,
          unloaded => unloaded.Path,
          errored => errored.Path
      );
}