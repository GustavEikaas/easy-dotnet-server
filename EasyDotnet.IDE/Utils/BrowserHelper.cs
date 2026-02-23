using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EasyDotnet.IDE.Utils;

public static class BrowserHelper
{
  public static Process? OpenBrowser(string url)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return Process.Start("xdg-open", url);
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      return Process.Start("open", url);
    }

    return null;
  }
}