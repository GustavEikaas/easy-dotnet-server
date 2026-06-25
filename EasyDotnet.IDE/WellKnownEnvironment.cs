namespace EasyDotnet.IDE;

public static class WellKnownEnvironment
{
  public static readonly EnvironmentVariable RoslynPath = new("EASY_DOTNET_ROSLYN_DLL_PATH");

  public static readonly EnvironmentVariable GodotBinPath = new("EASY_DOTNET_GODOT_BIN_PATH");

  public static readonly EnvironmentVariable DebuggerEngine = new("EASY_DOTNET_DEBUGGER_ENGINE");

  public static readonly EnvironmentVariable DebuggerBinPath = new("EASY_DOTNET_DEBUGGER_BIN_PATH");
}

public readonly record struct EnvironmentVariable(string Name)
{
  public string? Value
  {
    get
    {
      var val = Environment.GetEnvironmentVariable(Name);
      return !string.IsNullOrWhiteSpace(val) ? val : null;
    }
  }

  public string GetValueOrDefault(string defaultValue) => Value ?? defaultValue;

  public override string ToString() => Name;
}