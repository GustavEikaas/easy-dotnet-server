namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record InterceptableAttachRequest(
      int Seq,
      string Type,
      string Command,
      InterceptableAttachArguments Arguments,
      Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties = null
  ) : Request(Seq, Type, AdditionalProperties, Command);

  public record InterceptableAttachArguments(
      string? Request = null,
      string? Program = null,
      int? ProcessId = null,
      string? Cwd = null,
      Dictionary<string, string>? Env = null
  );
}