using System.Diagnostics;
using Microsoft.Extensions.Logging;
namespace EasyDotnet.IDE.Notifications;

internal static class OsNotify
{
  public static void TryNotifyTestRunFinished(ILogger logger, int passed, int failed, int skipped, int cancelled)
  {
    const string title = "Tests finished";
    var body = $"Passed: {passed}  Failed: {failed}  Skipped: {skipped}  Cancelled: {cancelled}";
    try
    {
      if (OperatingSystem.IsLinux())
        NotifyLinux(title, body);
      else if (OperatingSystem.IsMacOS())
        NotifyMacOS(title, body);
      else if (OperatingSystem.IsWindows())
        NotifyWindows(title, body);
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "OS notification failed (ignored)");
    }
  }

  private static void NotifyLinux(string title, string message)
  {
    var psi = new ProcessStartInfo("notify-send")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
    };
    psi.ArgumentList.Add(title);
    psi.ArgumentList.Add(message);
    using var proc = Process.Start(psi);
    if (proc is null) return;
    _ = proc.WaitForExit(750);
  }

  private static void NotifyMacOS(string title, string message)
  {

  }

  private static void NotifyWindows(string title, string message)
  {

  }
}