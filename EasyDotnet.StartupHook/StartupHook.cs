using System.IO.Pipes;

#pragma warning disable RCS1110 // Declare type inside namespace
internal static class StartupHook
#pragma warning restore RCS1110 // Declare type inside namespace
{
  public static void Initialize()
  {
    LogIfDebug($"ProcessId: {Environment.ProcessId}");
    LogIfDebug($"CurrentDirectory: {Environment.CurrentDirectory}");
    LogIfDebug($".NET Version: {Environment.Version}");

    var pipeName = Environment.GetEnvironmentVariable("EASY_DOTNET_HOOK_PIPE");
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

    LogIfDebug("Waiting for resume byte...");
    client.ReadByte();
    LogIfDebug("Received resume byte");
  }

  private static void LogIfDebug(string message)
  {
    var debug = Environment.GetEnvironmentVariable("EASY_DOTNET_HOOK_DEBUG") == "1";
    if (debug)
    {
      Console.WriteLine($"[StartupHook] {DateTime.UtcNow:O} | {message}");
    }
  }
}