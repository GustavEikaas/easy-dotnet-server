using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Services;

public class LspProgressReporterTests
{
  [Test]
  public async Task FastOperation_DoesNotEmitProgress()
  {
    var handler = new RecordingMessageHandler();
    using var rpc = new JsonRpc(handler);
    var reporter = new LspProgressReporter(
        rpc,
        new LspProgressOptions { Delay = TimeSpan.FromMilliseconds(50) },
        NullLogger<LspProgressReporter>.Instance);

    var result = await reporter.RunWithDelayedProgressAsync(
        "Searching NuGet",
        "Searching packages...",
        _ => Task.FromResult(42),
        default);

    await Assert.That(result).IsEqualTo(42);
    await Assert.That(handler.Writes.Count).IsEqualTo(0);
  }

  [Test]
  public async Task SlowOperation_EmitsBeginAndEndProgress()
  {
    var handler = new RecordingMessageHandler();
    using var rpc = new JsonRpc(handler);
    var reporter = new LspProgressReporter(
        rpc,
        new LspProgressOptions { Delay = TimeSpan.FromMilliseconds(1) },
        NullLogger<LspProgressReporter>.Instance);

    var result = await reporter.RunWithDelayedProgressAsync(
        "Searching NuGet",
        "Searching packages...",
        async ct =>
        {
          await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
          return 42;
        },
        default);

    await Assert.That(result).IsEqualTo(42);
    await Assert.That(handler.Writes.Count).IsEqualTo(2);
  }

  [Test]
  public async Task CancelledOperation_PropagatesCancellation()
  {
    var handler = new RecordingMessageHandler();
    using var rpc = new JsonRpc(handler);
    var reporter = new LspProgressReporter(
        rpc,
        new LspProgressOptions { Delay = TimeSpan.FromMilliseconds(1) },
        NullLogger<LspProgressReporter>.Instance);
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    await Assert.ThrowsAsync<OperationCanceledException>(() =>
        reporter.RunWithDelayedProgressAsync<int>(
            "Searching NuGet",
            "Searching packages...",
            async ct =>
            {
              await Task.Delay(TimeSpan.FromSeconds(1), ct);
              return 42;
            },
            cts.Token));
  }

  private sealed class RecordingMessageHandler : IJsonRpcMessageHandler
  {
    public List<JsonRpcMessage> Writes { get; } = [];
    public bool CanRead => false;
    public bool CanWrite => true;
    public IJsonRpcMessageFormatter Formatter { get; } = new JsonMessageFormatter();

    public ValueTask<JsonRpcMessage?> ReadAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException();

    public ValueTask WriteAsync(JsonRpcMessage jsonRpcMessage, CancellationToken cancellationToken)
    {
      Writes.Add(jsonRpcMessage);
      return ValueTask.CompletedTask;
    }
  }
}
