using System.IO.Pipes;

#pragma warning disable RCS1110 // Declare type inside namespace
internal static class StartupHook
#pragma warning restore RCS1110 // Declare type inside namespace
{
  public static void Initialize()
  {
    try
    {
      LogIfDebug($"ProcessId: {Environment.ProcessId}");
      LogIfDebug($"CurrentDirectory: {Environment.CurrentDirectory}");
      LogIfDebug($".NET Version: {Environment.Version}");
      var pipeName = ReadAndDiscardHookPipe();
      RemoveStartupHookEnvironment();

      LogIfDebug($"EASY_DOTNET_HOOK_PIPE: '{pipeName}'");
      if (string.IsNullOrEmpty(pipeName)) return;

      using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
      client.Connect(5000);
      LogIfDebug($"Pipe connected: {client.IsConnected}");

      var pid = Environment.ProcessId;
      LogIfDebug($"Sending PID: {pid}");
      var pidBytes = BitConverter.GetBytes(pid);
      LogIfDebug($"Writing {pidBytes.Length} bytes to pipe...");
      client.Write(pidBytes, 0, pidBytes.Length);
      client.Flush();
      LogIfDebug("PID written and flushed.");

      var resumeTimeout = GetResumeTimeout();
      LogIfDebug($"Waiting for resume byte (timeout {resumeTimeout.TotalSeconds:0}s)...");
      using var cts = new CancellationTokenSource(resumeTimeout);
      try
      {
        var buf = new byte[1];
        var read = client.ReadAsync(buf.AsMemory(0, 1), cts.Token).AsTask().GetAwaiter().GetResult();
        LogIfDebug(read == 0 ? "Pipe closed before resume byte; continuing" : "Received resume byte");
      }
      catch (OperationCanceledException)
      {
        // Don't hang the process forever if the resume signal never arrives (e.g. the debugger
        // failed to attach). Continue running un-attached rather than blocking startup indefinitely.
        LogIfDebug($"Timed out after {resumeTimeout.TotalSeconds:0}s waiting for resume byte; continuing without debugger attach");
      }
    }
    catch (Exception ex)
    {
      LogIfDebug($"Initialize failed: {ex.GetType().Name}: {ex.Message}");
      LogIfDebug(ex.StackTrace ?? "<no stack>");
      throw;
    }
  }

  private static TimeSpan GetResumeTimeout()
  {
    var raw = Environment.GetEnvironmentVariable("EASY_DOTNET_HOOK_RESUME_TIMEOUT_MS");
    return int.TryParse(raw, out var ms) && ms > 0
        ? TimeSpan.FromMilliseconds(ms)
        : TimeSpan.FromSeconds(30);
  }

  private static string? ReadAndDiscardHookPipe()
  {
    const string envVarName = "EASY_DOTNET_HOOK_PIPE";
    var pipeName = Environment.GetEnvironmentVariable(envVarName);
    Environment.SetEnvironmentVariable(envVarName, null);
    return pipeName;
  }

  private static void RemoveStartupHookEnvironment()
  {
    try
    {
      var currentHooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
      if (string.IsNullOrEmpty(currentHooks)) return;

      var hooksList = currentHooks.Split(Path.PathSeparator);

      var remainingHooks = string.Join(Path.PathSeparator.ToString(), hooksList.Skip(1));

      Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", string.IsNullOrEmpty(remainingHooks) ? null : remainingHooks);
      LogIfDebug("Successfully scrubbed hook from environment.");
    }
    catch (Exception ex)
    {
      LogIfDebug($"Failed to scrub environment: {ex.Message}");
    }
  }

  private static void LogIfDebug(string message)
  {
    var debug = Environment.GetEnvironmentVariable("EASY_DOTNET_HOOK_DEBUG") == "1";
    if (!debug) return;

    var line = $"[StartupHook] {DateTime.UtcNow:O} | {message}";
    Console.WriteLine(line);

    var logFile = Environment.GetEnvironmentVariable("EASY_DOTNET_HOOK_LOG_FILE");
    if (string.IsNullOrEmpty(logFile)) return;
    try
    {
      File.AppendAllText(logFile, line + Environment.NewLine);
    }
    catch
    {
    }
  }
}