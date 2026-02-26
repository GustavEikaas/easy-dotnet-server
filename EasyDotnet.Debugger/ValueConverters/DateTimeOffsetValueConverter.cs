using System.Globalization;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public sealed class DateTimeOffsetValueConverter(ILogger<DateTimeOffsetValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "DateTimeOffset";

  public override bool CanConvert(Variable val) => val.Type == "System.DateTimeOffset";

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

    if (!ValueConverterHelpers.TryGetLong(lookup, "Ticks", out var ticks) || !ValueConverterHelpers.TryGetInt(lookup, "TotalOffsetMinutes", out var offsetInMinutes))
    {
      LogFailure("Missing or invalid required DateTime fields", id);
      return response!;
    }
    
    var dateTimeOffset = new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetInMinutes));
    response.Body!.AssignComputedResult(dateTimeOffset.ToString());
    return response;
  }
}