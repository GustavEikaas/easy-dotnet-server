using System.Globalization;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class TimeOnlyValueConverter(ILogger<TimeOnlyValueConverter> logger)
    : ValueConverterBase(logger)
{
  protected override string ConverterName => "TimeOnly";

  public override bool CanConvert(Variable val) =>
      val.Type == "System.TimeOnly";

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
      return response;

    var lookup = ValueConverterHelpers.BuildFieldLookup(variables);

    ValueConverterHelpers.TryGetInt(lookup, "Hour", out var hour);
    ValueConverterHelpers.TryGetInt(lookup, "Minute", out var minute);
    ValueConverterHelpers.TryGetInt(lookup, "Second", out var second);
    ValueConverterHelpers.TryGetInt(lookup, "Millisecond", out var ms);
    ValueConverterHelpers.TryGetInt(lookup, "Microsecond", out var micro);
    ValueConverterHelpers.TryGetInt(lookup, "Nanosecond", out var nano);

    var ticksExtra = (micro * 10L) + (nano / 100L);

    var time = new TimeOnly(hour, minute, second, ms).Add(TimeSpan.FromTicks(ticksExtra));

    var precise = FormatPrecise(time);
    var localized = time.ToString("t", CultureInfo.CurrentCulture);

    response!.Body!.AssignComputedResult($"{precise} | {localized}");
    return response;
  }

  private static string FormatPrecise(TimeOnly time) => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
}