using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public class DateTimeValueConverter() : IValueConverter
{
  public bool CanConvert(Variable val) =>
      val.Type == "System.DateTime";

  public async Task<VariablesResponse> TryConvertAsync(
      int id,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var variablesResponse = await proxy.GetVariablesAsync(id, cancellationToken);
    if (variablesResponse?.Body?.Variables is null)
    {
      return variablesResponse ?? throw new Exception($"Failed to resolve variables by ID {id}");
    }

    int GetInt(string name) =>
        int.Parse(variablesResponse.Body.Variables.First(v => v.Name == name).Value);

    string GetString(string name) =>
        variablesResponse.Body.Variables.First(v => v.Name == name).Value;

    var year = GetInt("Year");
    var month = GetInt("Month");
    var day = GetInt("Day");
    var hour = GetInt("Hour");
    var minute = GetInt("Minute");
    var second = GetInt("Second");
    var millisecond = GetInt("Millisecond");
    var micro = GetInt("Microsecond");
    var nano = GetInt("Nanosecond");
    var kind = Enum.Parse<DateTimeKind>(GetString("Kind"));

    long extraTicks = (micro * 10) + (nano / 100);

    var dt = new DateTime(year, month, day, hour, minute, second, millisecond, kind).AddTicks(extraTicks);

    variablesResponse.Body.AssignComputedResult(dt == default ? "DateTime.MinValue" : $"{dt:O} ({dt.Kind})");

    return variablesResponse;
  }
}