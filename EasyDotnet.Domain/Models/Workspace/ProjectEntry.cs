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