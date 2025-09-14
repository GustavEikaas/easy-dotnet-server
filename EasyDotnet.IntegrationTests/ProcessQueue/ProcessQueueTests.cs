using System.Diagnostics;
using System.Runtime.InteropServices;
using EasyDotnet.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.IntegrationTests.ProcessQueue;


public class ProcessQueueServiceTests
{
  [Fact]
  public void ConcurrencyLimit_IsHonored()
  {
    var service = new ProcessQueueService(NullLogger<ProcessQueueService>.Instance, maxConcurrent: 2);

    var tasks = Enumerable.Range(0, 5).Select(async _ =>
    {
      var (command, args) = GetSleepCommand(5);
      await service.RunProcessAsync(command, args, cancellationToken: CancellationToken.None);
    });

    Assert.True(service.CurrentCount() == 2, $"Current running was {service.CurrentCount()}");
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

  [Fact]
  public async Task Timeout_CancelsLongRunningProcess()
  {
    var service = new ProcessQueueService(NullLogger<ProcessQueueService>.Instance, maxConcurrent: 1);

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

    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
        "Process should have been canceled early due to timeout");
  }

  [Fact]
  public async Task KillOnTimeout_KillsProcess_CrossPlatform()
  {
    var service = new ProcessQueueService(NullLogger<ProcessQueueService>.Instance, maxConcurrent: 1);

    var options = new ProcessOptions(
        KillOnTimeout: true,
        CancellationTimeout: TimeSpan.FromSeconds(1));

    var (cmd, args) = GetSleepCommand(10);

    await Assert.ThrowsAsync<OperationCanceledException>(async () => await service.RunProcessAsync(cmd, args, options, CancellationToken.None));
  }

}