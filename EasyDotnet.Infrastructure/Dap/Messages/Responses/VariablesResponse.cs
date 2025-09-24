namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class VariablesResponse : Response
  {
    public InterceptableVariablesResponseBody Body { get; set; } = new();
  }

  public class InterceptableVariablesResponseBody
  {
    public List<Variable> Variables { get; set; } = [];
  }

  public record Variable(string? EvaluateName, string Name, string? Type, string? Value, int VariablesReference, int? NamedVariables);
}