namespace EasyDotnet.ExternalConsole;

public sealed record InitializeRequest(string Program, string[] Args);

public sealed record InitializeResponse(int Pid);