using System.Globalization;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class DateOnlyValueConverter(ILogger<DateOnlyValueConverter> logger)
    : ValueConverterBase(logger)
{
  protected override string ConverterName => "DateOnly";

  public override bool CanConvert(Variable val) =>
      val.Type == "System.DateOnly";

  public override async Task<VariablesResponse> TryConvertAsync(
      int id,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var response = await proxy.GetVariablesAsync(id, cancellationToken);

    if (response == null)
    {
      LogFailure("Proxy returned null response", id);
      throw new InvalidOperationException(
          $"Failed to get variables for reference {id}");
    }

    if (!ValidateResponse(response, id, out var variables))
      return response;

    var lookup = ValueConverterHelpers.BuildFieldLookup(variables);

    ValueConverterHelpers.TryGetInt(lookup, "Year", out var year);
    ValueConverterHelpers.TryGetInt(lookup, "Month", out var month);
    ValueConverterHelpers.TryGetInt(lookup, "Day", out var day);

    var date = new DateOnly(year, month, day);

    var iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var localized = date.ToString("D", CultureInfo.CurrentCulture);

    response!.Body!.AssignComputedResult($"{iso} | {localized}");
    return response;
  }
}