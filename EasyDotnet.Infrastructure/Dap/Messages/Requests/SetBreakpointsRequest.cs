namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class SetBreakpointsRequest : Request
  {
    public required SetBreakpointsArguments Arguments { get; set; }
  }

  public class SetBreakpointsArguments
  {
    public required List<Breakpoint> Breakpoints { get; set; }
    public required List<int> Lines { get; set; }
    public required Source Source { get; set; }
    public required bool SourceModified { get; set; }
  }

  public class Breakpoint
  {
    public required int Line { get; set; }
  }

  public class Source
  {
    public required string Name { get; set; }
    public required string Path { get; set; }
  }
}