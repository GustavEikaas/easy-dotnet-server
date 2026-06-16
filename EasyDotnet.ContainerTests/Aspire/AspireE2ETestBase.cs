using System.Collections.Concurrent;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Aspire;

/// <summary>
/// Base for the full Aspire DCP end-to-end test. Unlike <see cref="AspireRunTestBase{T}"/> (which
/// only acknowledges run commands), this test base acts as a real editor: every
/// <c>runCommandManaged</c> reverse request is actually executed <em>inside the container</em>
/// (same place the projects were built, where dcp lives, and where loopback to the in-container
/// DCP server works). Running the AppHost this way makes dcp start and drive <c>/run_session</c>
/// for each resource, which arrive as further <c>runCommandManaged</c> requests.
/// </summary>
public abstract class AspireE2ETestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private readonly Channel<TestTrackedJob> _runCommands = Channel.CreateUnbounded<TestTrackedJob>();
  private readonly ConcurrentQueue<string> _displayErrors = new();

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>
  /// Reads <c>runCommandManaged</c> requests until the working directory of each expected resource
  /// name has been seen, or the timeout elapses. The AppHost run command (and any others) are
  /// executed too; only the expected resource names gate completion.
  /// </summary>
  protected async Task<IReadOnlyCollection<string>> WaitForResourceRunsAsync(string[] expected, TimeSpan timeout)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using var cts = new CancellationTokenSource(timeout);
    try
    {
      while (!expected.All(seen.Contains))
      {
        var job = await _runCommands.Reader.ReadAsync(cts.Token);
        foreach (var name in expected)
        {
          if (MatchesProject(job.Command.WorkingDirectory, name))
          {
            seen.Add(name);
          }
        }
      }
    }
    catch (OperationCanceledException)
    {
      var missing = string.Join(", ", expected.Where(e => !seen.Contains(e)));
      throw new XunitException(
        $"Did not observe resource run commands for [{missing}] within {timeout.TotalSeconds:0}s.{CollectPendingErrors()}");
    }
    return seen;
  }

  private static bool MatchesProject(string workingDirectory, string name) =>
    workingDirectory.Replace('\\', '/').TrimEnd('/').EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);

  private string CollectPendingErrors()
  {
    var errors = _displayErrors.ToArray();
    return errors.Length == 0 ? string.Empty : "\n Pending errors:\n  " + string.Join("\n  ", errors);
  }

  /// <summary>
  /// Runs the command the server asked for inside the container (exec form: no shell, so env
  /// values need no quoting). Blocks until exit, then reports the exit code back to the server.
  /// </summary>
  private async Task ExecInContainerAsync(TestTrackedJob job)
  {
    var command = new List<string> { "env", "-C", job.Command.WorkingDirectory };
    foreach (var kv in job.Command.EnvironmentVariables)
    {
      command.Add($"{kv.Key}={kv.Value}");
    }
    command.Add(job.Command.Executable);
    command.AddRange(job.Command.Arguments);

    try
    {
      var result = await Container.ExecAsync(command);
      await Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode = result.ExitCode });
    }
    catch
    {
      // Container torn down or RPC gone while a long-running process was still executing.
    }
  }

  private sealed class RpcHandlers(AspireE2ETestBase<TContainer> test)
  {
    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<RunCommandResponse> RunCommandManaged(TestTrackedJob job)
    {
      test._runCommands.Writer.TryWrite(job);
      _ = test.ExecInContainerAsync(job);
      return Task.FromResult(new RunCommandResponse(0));
    }

    [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
    public void DisplayError(TestDisplayMessage message) => test._displayErrors.Enqueue(message.Message);

    // A solution-less workspace with an explicit filePath resolves the AppHost directly, so no
    // picker is expected; if one appears, prefer the AppHost option to keep the test deterministic.
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public string? PromptSelection(TestPromptSelectionRequest request) =>
      Array.Find(request.Choices, c => c.Display.Contains("AppHost", StringComparison.OrdinalIgnoreCase))?.Id
        ?? request.Choices.FirstOrDefault()?.Id;
  }
}