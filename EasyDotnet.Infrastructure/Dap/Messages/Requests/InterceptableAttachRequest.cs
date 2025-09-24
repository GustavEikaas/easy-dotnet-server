namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class InterceptableAttachRequest : Request
  {
    public InterceptableAttachArguments Arguments { get; set; } = new();
  }

  public class InterceptableAttachArguments
  {
    public string? Request { get; set; }
    public string? Program { get; set; }
    public int? ProcessId { get; set; }
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Env { get; set; }
  }
}