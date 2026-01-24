using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class GuidValueConverter(ILogger<GuidValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "Guid";

  public override bool CanConvert(Variable val) => val.Type == "System.Guid";

  public override async Task<VariablesResponse> TryConvertAsync(
    int id,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    var response = await proxy.GetVariablesAsync(id, cancellationToken);

    if (response == null)
    {
      LogFailure("Proxy returned null response", id);
      throw new InvalidOperationException($"Failed to get variables for reference {id}");
    }

    if (!ValidateResponse(response, id, out var variables))
    {
      return response;
    }

    var lookup = ValueConverterHelpers.BuildFieldLookup(variables);

    if (!ValueConverterHelpers.TryGetInt(lookup, "_a", out var a) ||
        !ValueConverterHelpers.TryGetShort(lookup, "_b", out var b) ||
        !ValueConverterHelpers.TryGetShort(lookup, "_c", out var c) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_d", out var d) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_e", out var e) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_f", out var f) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_g", out var g) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_h", out var h) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_i", out var i) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_j", out var j) ||
        !ValueConverterHelpers.TryGetByte(lookup, "_k", out var k))
    {
      LogFailure("Missing or invalid GUID fields", id);
      return response;
    }

    try
    {
      var guid = new Guid(a, b, c, d, e, f, g, h, i, j, k);

      var formatted = guid == Guid.Empty
        ? $"Guid.Empty ({Guid.Empty:D})"
        : guid.ToString("D");

      response.Body!.AssignComputedResult(formatted);
      return response;
    }
    catch (ArgumentException ex)
    {
      LogFailure($"Invalid GUID constructor arguments: {ex.Message}", id);
      return response;
    }
  }
}