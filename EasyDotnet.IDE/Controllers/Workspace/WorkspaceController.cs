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

    // Entering the scope sends the "begin" notification
    using (var progress = new ProgressScope(clientService, "MSBuild", $"Building {selected.Display}..."))
    {
      // --- ARTIFICIAL TESTING BLOCK START ---
      var stages = new[] { "Parsing project...", "Compiling source...", "Optimizing...", "Linking..." };
      for (var i = 0; i < stages.Length; i++)
      {
        cancellationToken.ThrowIfCancellationRequested();

        var percentage = (i + 1) * 25;
        progress.Report(stages[i], percentage);

        // Wait 1 second between stages to see the UI update
        await Task.Delay(4000, cancellationToken);
      }
      // --- ARTIFICIAL TESTING BLOCK END ---

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
    } // Disposing here sends the "end" notification
  }

  private SelectionOption<ProjectEntry>[] GetSolutionOption()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    return string.IsNullOrEmpty(sln) ? [] : [sln.FromSolutionFile<ProjectEntry>() with { Data = new ProjectEntry.Unloaded(sln) }];
  }
}
