namespace EasyDotnet.Aspire.Contracts;

public static class AspireRpcMethods
{
  public const string Launch = "aspire/launch";
  public const string Shutdown = "aspire/shutdown";
  public const string RunManagedResource = "aspire/runManagedResource";
  public const string StopManagedResource = "aspire/stopManagedResource";
  public const string ReportProcessId = "aspire/reportProcessId";
}
public sealed record ReportProcessIdRequest(string RunId, int Pid);

public static class AspireRunIds
{
  public const string AppHost = "apphost";
}

public sealed record LaunchAppHostRequest(
    string AppHostProjectPath,
    bool Debug = false,
    Dictionary<string, string>? EnvironmentVariables = null);

public sealed record LaunchAppHostResponse(bool Started);

public sealed record RunManagedResourceRequest(
    string RunId,
    string ProjectPath,
    List<string>? Args,
    Dictionary<string, string> EnvironmentVariables,
    bool Debug = false);

public sealed record RunManagedResourceResponse(int ExitCode);

public sealed record StopManagedResourceRequest(string RunId);