using System.Globalization;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class DateTimeValueConverter(ILogger<DateTimeValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "DateTime";

  public override bool CanConvert(Variable val) => val.Type == "System.DateTime";

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

    if (!ValueConverterHelpers.TryGetInt(lookup, "Year", out var year) ||
        !ValueConverterHelpers.TryGetInt(lookup, "Month", out var month) ||
        !ValueConverterHelpers.TryGetInt(lookup, "Day", out var day) ||
        !ValueConverterHelpers.TryGetInt(lookup, "Hour", out var hour) ||
        !ValueConverterHelpers.TryGetInt(lookup, "Minute", out var minute) ||
        !ValueConverterHelpers.TryGetInt(lookup, "Second", out var second) ||
        !ValueConverterHelpers.TryGetInt(lookup, "Millisecond", out var millisecond) ||
        !ValueConverterHelpers.TryGetEnum<DateTimeKind>(lookup, "Kind", out var kind))
    {
      LogFailure("Missing or invalid required DateTime fields", id);
      return response!;
    }

    ValueConverterHelpers.TryGetInt(lookup, "Microsecond", out var micro);
    ValueConverterHelpers.TryGetInt(lookup, "Nanosecond", out var nano);

    try
    {
      long extraTicks = (micro * 10) + (nano / 100);
      var dt = new DateTime(year, month, day, hour, minute, second, millisecond, kind)
        .AddTicks(extraTicks);

      var formatted = dt == default
        ? "DateTime. MinValue (0001-01-01T00:00:00. 0000000)"
        : FormatDateTime(dt);

      response!.Body!.AssignComputedResult(formatted);
      return response;
    }
    catch (ArgumentOutOfRangeException ex)
    {
      LogFailure($"Invalid DateTime values: {ex.Message}", id);
      return response!;
    }
  }

  private static string FormatDateTime(DateTime dt)
  {
    var iso = dt.ToString("O", CultureInfo.InvariantCulture);

    var localized = dt.ToString("F", CultureInfo.CurrentCulture);

    var relative = GetRelativeTime(dt);

    return relative != null
      ? $"{iso} | {localized} | {relative}"
      : $"{iso} | {localized}";
  }

  private static string? GetRelativeTime(DateTime dt)
  {
    var now = dt.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now;
    var diff = now - dt;

    if (Math.Abs(diff.TotalDays) > 7)
    {
      return null;
    }

    return diff.TotalSeconds switch
    {
      < 60 => $"{(int)diff.TotalSeconds} seconds ago",
      < 3600 => $"{(int)diff.TotalMinutes} minutes ago",
      < 86400 => $"{(int)diff.TotalHours} hours ago",
      _ => $"{(int)diff.TotalDays} days ago"
    };
  }
}