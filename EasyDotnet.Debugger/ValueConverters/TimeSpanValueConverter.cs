using System.Globalization;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class TimeSpanValueConverter(ILogger<TimeSpanValueConverter> logger)
    : ValueConverterBase(logger)
{
  protected override string ConverterName => "TimeSpan";

  public override bool CanConvert(Variable val) =>
      val.Type == "System.TimeSpan";

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

    var ticks = long.Parse(lookup["Ticks"], CultureInfo.InvariantCulture);

    ValueConverterHelpers.TryGetInt(lookup, "Days", out var days);
    ValueConverterHelpers.TryGetInt(lookup, "Hours", out var hours);
    ValueConverterHelpers.TryGetInt(lookup, "Minutes", out var minutes);
    ValueConverterHelpers.TryGetInt(lookup, "Seconds", out var seconds);
    ValueConverterHelpers.TryGetInt(lookup, "Milliseconds", out var ms);
    ValueConverterHelpers.TryGetInt(lookup, "Microseconds", out var micro);
    ValueConverterHelpers.TryGetInt(lookup, "Nanoseconds", out var nano);

    var ts = new TimeSpan(ticks);

    var primary = ts.ToString("c", CultureInfo.InvariantCulture);
    var readable = FormatReadable(days, hours, minutes, seconds, ms, micro, nano);

    response!.Body!.AssignComputedResult($"{primary} | {readable}");
    return response;
  }

  private static string FormatReadable(
      int days, int hours, int minutes, int seconds,
      int ms, int micro, int nano)
  {
    var ns = (micro * 1000L) + nano;

    var units = new (string Name, long Value)[]
    {
            ("days", days),
            ("hours", hours),
            ("minutes", minutes),
            ("seconds", seconds),
            ("ms", ms),
            ("ns", ns)
    };

    var start = 0;
    while (start < units.Length - 1 && units[start].Value == 0)
      start++;

    return string.Join(", ",
        units
            .Skip(start)
            .Select(u => $"{u.Value} {u.Name}")
    );
  }
}