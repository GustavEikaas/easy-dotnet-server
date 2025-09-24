namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record VariablesRequest(
      int Seq,
      string Type,
      string Command,
      VariablesRequestArguments Arguments,
      Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties = null
  ) : Request(Seq, Type, AdditionalProperties, Command);

  public record VariablesRequestArguments(int VariablesReference);
}