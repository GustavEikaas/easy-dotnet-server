namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record SetBreakpointsRequest(
      int Seq,
      string Type,
      string Command,
      SetBreakpointsArguments Arguments,
      Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties = null
  ) : Request(Seq, Type, AdditionalProperties, Command);

  public record Breakpoint(int Line);

  public record Source(
      string Name,
      string Path
  );

  public record SetBreakpointsArguments(
      List<Breakpoint> Breakpoints,
      List<int> Lines,
      Source Source,
      bool SourceModified
  );

}