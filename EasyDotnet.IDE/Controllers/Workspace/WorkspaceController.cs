using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.Domain.Models.Workspace;
using EasyDotnet.IDE.Controllers.MsBuild;
using EasyDotnet.IDE.Extensions;
using EasyDotnet.Infrastructure.Workspace;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Workspace;

public class WorkspaceController(IClientService clientService, WorkspaceService workspaceService, IMsBuildService msBuildService) : BaseController
{
  [JsonRpcMethod("workspace/build")]
  public async Task<BuildResultResponse?> Build(CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();

    var projects = await workspaceService.LazyLoadProjectsAsync(TimeSpan.FromSeconds(0));
    var choices = GetSolutionOption()
        .Concat(projects.Select(e => e.ToSelectionOption()))
        .ToArray();

    var selected = await clientService.RequestSelection("Select project to build", choices);

    if (selected?.Data is null) return null;

    if (selected.Data is ProjectEntry.Errored err)
    {
      throw new LocalRpcException($"Cannot build: {err.ErrorMessage}");
    }

    using (new ProgressScope(clientService, "MSBuild", $"Building {selected.Display}..."))
    {
      var result = await msBuildService.RequestBuildAsync(
          selected.Data.GetPath(),
          null,
          null,
          "Debug",
          cancellationToken
      );

      return new BuildResultResponse(
          result.Success,
          result.Errors.ToBatchedAsyncEnumerable(50),
          result.Warnings.ToBatchedAsyncEnumerable(50)
      );
    }
  }

  private SelectionOption<ProjectEntry>[] GetSolutionOption()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    return string.IsNullOrEmpty(sln) ? [] : [sln.FromSolutionFile<ProjectEntry>() with { Data = new ProjectEntry.Unloaded(sln) }];
  }
}
