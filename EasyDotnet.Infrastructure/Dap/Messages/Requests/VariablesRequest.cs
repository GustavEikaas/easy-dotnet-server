namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class VariablesRequest : Request
  {
    public VariablesRequestArguments Arguments { get; set; } = new();
  }

  public class VariablesRequestArguments
  {
    public int VariablesReference { get; set; }
  }
}