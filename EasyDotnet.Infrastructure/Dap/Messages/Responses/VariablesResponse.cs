namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{

  public record VariablesResponse(
      int Seq,
      string Type,
      Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties,
      int RequestSeq,
      bool Success,
      string Command,
      string? Message,
      InterceptableVariablesResponseBody Body
  ) : Response(Seq, Type, AdditionalProperties, RequestSeq, Success, Command, Message);

  public record InterceptableVariablesResponseBody(
      List<Variable> Variables
  );

  public record Variable(string? EvaluateName, string Name, string? Type, string? Value, int VariablesReference, int? NamedVariables);
}