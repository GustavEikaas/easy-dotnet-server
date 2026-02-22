using System.IO.Pipes;

#pragma warning disable RCS1110 // Declare type inside namespace
internal static class StartupHook
#pragma warning restore RCS1110 // Declare type inside namespace
{
  public static void Initialize()
  {
    var pipeName = Environment.GetEnvironmentVariable("EASY_DOTNET_HOOK_PIPE");
    if (string.IsNullOrEmpty(pipeName)) return;

    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
    client.Connect(5000);

    var pid = Environment.ProcessId;
    var pidBytes = BitConverter.GetBytes(pid);
    client.Write(pidBytes, 0, pidBytes.Length);
    client.Flush();

    client.ReadByte();
  }
}