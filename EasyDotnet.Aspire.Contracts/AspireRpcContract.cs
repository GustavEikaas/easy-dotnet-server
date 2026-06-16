namespace EasyDotnet.Aspire.Contracts;

/// <summary>
/// JSON-RPC method names for the thin IDE &lt;-&gt; Aspire named-pipe channel.
/// Mirrors the BuildServer split: the IDE spawns the Aspire host and drives it,
/// while the Aspire host calls back to fulfil run sessions through the editor.
/// </summary>
public static class AspireRpcMethods
{
  // IDE -> Aspire (the IDE drives the host)
  public const string Launch = "aspire/launch";
  public const string Shutdown = "aspire/shutdown";

  // Aspire -> IDE (the host asks the editor to run/stop a managed resource)
  public const string RunManagedResource = "aspire/runManagedResource";
  public const string StopManagedResource = "aspire/stopManagedResource";

  // IDE -> Aspire (the editor reports the OS pid it captured for a run session)
  public const string ReportProcessId = "aspire/reportProcessId";
}

/// <summary>
/// IDE -&gt; Aspire. Reports the OS process id captured (via the startup hook) for a run session,
/// so the host can emit a <c>processRestarted</c> notification to DCP (otherwise the dashboard
/// shows pid 0). Ignored for runIds that are not DCP run sessions (e.g. the AppHost itself).
/// </summary>
public sealed record ReportProcessIdRequest(string RunId, int Pid);

/// <summary>Well-known run-session ids. The AppHost is the parent of all resource run sessions.</summary>
public static class AspireRunIds
{
  public const string AppHost = "apphost";
}

/// <summary>
/// IDE -&gt; Aspire. Starts a DCP server and launches the given AppHost project so
/// that DCP connects back to it via the injected <c>DEBUG_SESSION_*</c> env vars.
/// When <paramref name="Debug"/> is true the AppHost is launched with
/// <c>DEBUG_SESSION_RUN_MODE=Debug</c>, so DCP requests Debug mode for resources.
/// <para>
/// <paramref name="EnvironmentVariables"/> carries any extra env vars from the
/// selected launch profile (e.g. ASPNETCORE_ENVIRONMENT). They are merged into
/// the process environment before the <c>DEBUG_SESSION_*</c> vars, so the DCP
/// session vars always take precedence.
/// </para>
/// </summary>
public sealed record LaunchAppHostRequest(
    string AppHostProjectPath,
    bool Debug = false,
    Dictionary<string, string>? EnvironmentVariables = null);

public sealed record LaunchAppHostResponse(bool Started);

/// <summary>
/// Aspire -&gt; IDE. Asks the editor to run a project (an AppHost or a DCP resource)
/// and blocks until it exits; the completion of this call <em>is</em> the exit
/// signal (no separate back-channel).
/// <para>
/// The Aspire host does NOT build a command line — it relays the launch-config
/// essentials and the IDE resolves the real run command via its existing project
/// evaluation + <c>WorkspaceRunCommandBuilder</c> machinery, then runs it through
/// <c>EditorService.StartRunProjectAsync</c> on the Neovim side.
/// </para>
/// </summary>
public sealed record RunManagedResourceRequest(
    string RunId,
    string ProjectPath,
    List<string>? Args,
    Dictionary<string, string> EnvironmentVariables,
    bool Debug = false);

public sealed record RunManagedResourceResponse(int ExitCode);

/// <summary>Aspire -&gt; IDE. Stops a previously started managed resource.</summary>
public sealed record StopManagedResourceRequest(string RunId);