namespace EasyDotnet.ExternalConsole;

public sealed record InitializeRequest(
  string Program,
  string[] Args,
  string? Cwd,
  Dictionary<string, string>? Env
);

public sealed record InitializeResponse(int Pid);