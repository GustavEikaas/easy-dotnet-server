using System.Runtime.CompilerServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Extensions;
using EasyDotnet.IDE.MTP.RPC;
using EasyDotnet.Infrastructure;
using EasyDotnet.MsBuild;
using EasyDotnet.MTP;
using EasyDotnet.Types;

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

    await using var client = await Client.CreateAsync(testExecutablePath);
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
    await using var client = await Client.CreateAsync(testExecutablePath);

    var session = await debugOrchestrator.StartClientDebugSessionAsync(
      project.MSBuildProjectFullPath!,
      new(project.MSBuildProjectFullPath!, null, null, null),
      debugStrategyFactory.CreateStandardAttachStrategy(client.DebugeeProcessId),
      token);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.WaitForConfigurationDoneAsync();

    await foreach (var update in client.RunTestsAsync(filter, token))
    {
      yield return update.ToTestRunResult();
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

    await using var client = await Client.CreateAsync(testExecutablePath);

    await foreach (var update in client.RunTestsAsync(filter, token))
    {
      yield return update.ToTestRunResult();
    }
  }
}