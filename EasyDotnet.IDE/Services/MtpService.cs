using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Test;
using EasyDotnet.Extensions;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using EasyDotnet.MsBuild;
using EasyDotnet.MTP;
using EasyDotnet.MTP.RPC;

namespace EasyDotnet.Services;

public class MtpService(
  IEditorService editorService,
  IDebugStrategyFactory debugStrategyFactory,
  IDebugOrchestrator debugOrchestrator)
{
  public async Task<List<DiscoveredTest>> RunDiscoverAsync(string testExecutablePath, CancellationToken token)
  {
    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }

    await using var client = await Client.CreateAsync(testExecutablePath);
    var discovered = await client.DiscoverTestsAsync(token);
    var tests = discovered.Where(x => x != null && x.Node != null).Select(Domain.Models.MTP.TestNodeExtensions.ToDiscoveredTest).ToList();
    return tests;
  }

  public async Task<List<TestRunResult>> DebugTestsAsync(DotnetProject project, string testExecutablePath, RunRequestNode[] filter, CancellationToken token)
  {
    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }
    await using var client = await Client.CreateAsync(testExecutablePath);

    var session = await debugOrchestrator.StartClientDebugSessionAsync(
      project.MSBuildProjectFullPath!,
      new(project.MSBuildProjectFullPath!, null, null, null),
      debugStrategyFactory.CreateStandardAttachStrategy(client.DebugeeProcessId),
      token);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.ProcessStarted;

    var runResults = await client.RunTestsAsync(filter, token);
    var results = runResults.Select(x => x.ToTestRunResult()).ToList();
    return results;
  }

  public async Task<List<TestRunResult>> RunTestsAsync(string testExecutablePath, RunRequestNode[] filter, CancellationToken token)
  {
    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }
    await using var client = await Client.CreateAsync(testExecutablePath);
    var runResults = await client.RunTestsAsync(filter, token);
    var results = runResults.Select(x => x.ToTestRunResult()).ToList();
    return results;
  }
}