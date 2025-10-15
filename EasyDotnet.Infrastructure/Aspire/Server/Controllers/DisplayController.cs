using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class DisplayController
{
  [JsonRpcMethod("displaySuccess")]
  public void DisplaySuccess(string token, string message)
  {
    Console.WriteLine($"[{token}] Success: {message}");
  }

  [JsonRpcMethod("displaySubtleMessage")]
  public void DisplaySubtleMessage(string token, string message)
  {
    Console.WriteLine($"[{token}] {message}");
  }

  [JsonRpcMethod("displayEmptyLine")]
  public void DisplayEmptyLine(string token)
  {
    Console.WriteLine($"[{token}] Display empty line");
  }

  [JsonRpcMethod("displayError")]
  public void DisplayError(string token, string error)
  {
    Console.WriteLine($"[{token}] ERROR: {error}");
  }

  [JsonRpcMethod("displayPlainText")]
  public void DisplayPlainText(string token, string text)
  {
    Console.WriteLine($"[{token}] Plain text: {text}");
  }

  [JsonRpcMethod("displayLines")]
  public void DisplayLines(string token, DisplayLineState[] lines)
  {
    Console.WriteLine($"[{token}] Displaying lines:");
    foreach (var line in lines)
    {
      Console.WriteLine($"[{line.Stream}]: {line.Line}");
    }
  }

  [JsonRpcMethod("displayDashboardUrls")]
  public void DisplayDashboardUrls(string token, DashboardUrlsState dashboardUrls)
  {
    Console.WriteLine($"[{token}] Dashboard URLs received: {dashboardUrls.BaseUrlWithLoginToken}");
  }

  [JsonRpcMethod("displayCancellationMessage")]
  public void DisplayCancellationMessage(string token)
  {
    Console.WriteLine($"[{token}] Operation was cancelled by the user.");
  }

  [JsonRpcMethod("displayIncompatibleVersionError")]
  public void DisplayIncompatibleVersionError(string token, string requiredCapability, string appHostHostingSdkVersion)
  {
    Console.WriteLine($"[{token}] Incompatible version error:");
    Console.WriteLine($"  Required capability: {requiredCapability}");
    Console.WriteLine($"  AppHost hosting SDK version: {appHostHostingSdkVersion}");
  }

  [JsonRpcMethod("showStatus")]
  public void ShowStatus(string token, string? status)
  {
    Console.WriteLine($"[{token}] Status update: {status ?? "<none>"}");
  }
}

public sealed record DisplayLineState(string Stream, string Line);

public sealed record DashboardUrlsState(string? BaseUrlWithLoginToken, string? CodespacesUrlWithLoginToken)
{
  public bool DashboardHealthy { get; init; } = true;
}