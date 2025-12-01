using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class ListValueConverter() : IValueConverter
{
  public bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && AnyList().IsMatch(
        val.Type);

  public async Task<VariablesResponse> TryConvertAsync(int id, IDebuggerProxy proxy, CancellationToken cancellationToken)
  {
    var internals = await proxy.GetVariablesAsync(id, cancellationToken) ?? throw new Exception($"variables request for {id} returned null");
    var itemsVariable = internals.Body!.Variables.FirstOrDefault(v => v.Name == "_items");
    var x = itemsVariable?.GetType();
    var sizeVariable = internals.Body!.Variables.FirstOrDefault(v => v.Name == "_size");

    if (itemsVariable?.VariablesReference.HasValue != true ||
        itemsVariable?.VariablesReference <= 0)
    {
      return internals;
    }

    if (sizeVariable == null || !int.TryParse(sizeVariable.Value, out var actualSize))
    {
      return internals;
    }

    var itemsArrayResponse = await proxy.GetVariablesAsync(itemsVariable!.VariablesReference!.Value, cancellationToken);

    if (itemsArrayResponse?.Body?.Variables == null)
    {
      return internals;
    }

    internals.Body.Variables = [.. itemsArrayResponse.Body.Variables
      .Where(v => v.Name.StartsWith('[') && v.Name.EndsWith(']'))
      .OrderBy(v => ParseArrayIndex(v.Name))
      .Take(actualSize)];

    return internals;
  }

  private static int ParseArrayIndex(string name)
  {
    var trimmed = name.Trim('[', ']');
    return int.TryParse(trimmed, out var index) ? index : -1;
  }

  [GeneratedRegex(@"^System\.Collections\.Generic\.List<.*>$", RegexOptions.Compiled)]
  private static partial Regex AnyList();
}