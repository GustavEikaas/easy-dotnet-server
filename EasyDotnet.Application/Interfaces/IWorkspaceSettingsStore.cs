using EasyDotnet.Domain.Models.IDE;

namespace EasyDotnet.Application.Interfaces;

public interface IWorkspaceSettingsStore
{
  static abstract void ResetAll();
  void DeleteCurrentWorkspace();
  WorkspaceProjectReference? GetDefaultBuildProject();
  WorkspaceProjectReference? GetDefaultDebugProject();
  WorkspaceProjectReference? GetDefaultRunProject();
  WorkspaceProjectReference? GetDefaultViewProject();
  void SetDefaultBuildProject(WorkspaceProjectReference? value);
  void SetDefaultDebugProject(WorkspaceProjectReference? value);
  void SetDefaultRunProject(WorkspaceProjectReference? value);
  void SetDefaultViewProject(WorkspaceProjectReference? value);
}