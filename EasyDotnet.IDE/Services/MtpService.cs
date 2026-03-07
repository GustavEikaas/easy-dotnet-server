using System.Runtime.CompilerServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.TestRunner.Adapters;
using EasyDotnet.IDE.TestRunner.Adapters.MTP;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.Infrastructure;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.Services;

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

    await using var client = await MtpClient.CreateAsync(testExecutablePath);
    var discovered = await client.DiscoverTestsAsync(token).ToListAsync(token);
    return [.. discovered.Where(x => x != null && x.Node != null).Select(x => x.ToDiscoveredTest())];
  }

  public async IAsyncEnumerable<TestRunResult> DebugTestsAsync(
    DotnetProject project,
    string testExecutablePath,
    RunRequestNode[] filter,
    [EnumeratorCancellation] CancellationToken token)
  {
    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }
    await using var client = await MtpClient.CreateAsync(testExecutablePath);

    var session = await debugOrchestrator.StartClientDebugSessionAsync(
      project.MSBuildProjectFullPath!,
      new(project.MSBuildProjectFullPath!, null, null, null),
      debugStrategyFactory.CreateStandardAttachStrategy(client.DebugeeProcessId),
      token);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.ProcessStarted;
    await Task.Delay(1000, token);

    await foreach (var update in client.RunTestsAsync(filter, token))
    {
      yield return update.ToTestRunResult()!;
    }
  }

  public async IAsyncEnumerable<TestRunResult> RunTestsAsync(
    string testExecutablePath,
    RunRequestNode[] filter,
    [EnumeratorCancellation] CancellationToken token)
  {
    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }

    await using var client = await MtpClient.CreateAsync(testExecutablePath);

    await foreach (var update in client.RunTestsAsync(filter, token))
    {
      yield return update.ToTestRunResult()!;
    }
  }
}