using System.Diagnostics;
using System.Runtime.InteropServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.Infrastructure.Tests.Process;

public class ProcessQueueTests
{
  [Test]
  public async Task ConcurrencyLimit_IsHonored()
  {
    var service = new ProcessQueue(2, NullLogger<ProcessQueue>.Instance);

    var tasks = Enumerable.Range(0, 5).Select(async _ =>
    {
      var (command, args) = GetSleepCommand(5);
      await service.RunProcessAsync(command, args, cancellationToken: CancellationToken.None);
    });

    await Assert.That(service.CurrentCount()).IsEqualTo(2);
  }

  [Test]
  public async Task Timeout_CancelsLongRunningProcess()
  {
    var service = new ProcessQueue(1, NullLogger<ProcessQueue>.Instance);

    var options = new ProcessOptions(
        KillOnTimeout: false,
        CancellationTimeout: TimeSpan.FromSeconds(1));

    var sw = Stopwatch.StartNew();
    var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "127.0.0.1 -n 5"     // Windows
        : "-c 5 127.0.0.1";    // Linux/macOS
    await Assert.ThrowsAsync<OperationCanceledException>(async () => await service.RunProcessAsync(
              "ping",
              args,
              options,
              CancellationToken.None));
    sw.Stop();

    await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
  }

  [Test]
  public async Task KillOnTimeout_KillsProcess_CrossPlatform()
  {
    var service = new ProcessQueue(1, NullLogger<ProcessQueue>.Instance);

    var options = new ProcessOptions(
        KillOnTimeout: true,
        CancellationTimeout: TimeSpan.FromSeconds(1));

    var (cmd, args) = GetSleepCommand(10);

    await Assert.ThrowsAsync<OperationCanceledException>(async () => await service.RunProcessAsync(cmd, args, options, CancellationToken.None));
  }

  private static (string Command, string Arguments) GetSleepCommand(int seconds)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return ("powershell", $"-Command \"Start-Sleep -Seconds {seconds}\"");
    }
    else
    {
      return ("bash", $"-c \"sleep {seconds}\"");
    }
  }
}