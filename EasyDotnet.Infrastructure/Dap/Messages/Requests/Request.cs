namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class Request : ProtocolMessage
  {
    public required string Command { get; set; }
  }
}