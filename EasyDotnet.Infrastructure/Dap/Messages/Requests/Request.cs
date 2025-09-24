namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record Request(
    int Seq,
    string Type,
    Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties,
    string Command
    ) : ProtocolMessage(Seq, Type, AdditionalProperties);
}