using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class ListValueConverter(ILogger<IValueConverter> logger) : IValueConverter
{
  public bool CanConvert(VariablesResponse response)
  {
    if (response.Body?.Variables == null || response.Body.Variables.Count == 0)
    {
      return false;
    }

    var hasItems = response.Body.Variables.Any(v => v.Name == "_items");
    var hasSize = response.Body.Variables.Any(v => v.Name == "_size");
    var hasCapacity = response.Body.Variables.Any(v => v.Name == "Capacity");

    return hasItems && hasSize && hasCapacity;
  }

  public async Task<bool> TryConvertAsync(VariablesResponse response, IDebuggerProxy proxy, CancellationToken cancellationToken)
  {

    var itemsVariable = response.Body!.Variables.FirstOrDefault(v => v.Name == "_items");
    var sizeVariable = response.Body!.Variables.FirstOrDefault(v => v.Name == "_size");
    logger.LogInformation("Detected List internal structure, simplifying...");

    if (itemsVariable?.VariablesReference.HasValue != true ||
        itemsVariable?.VariablesReference <= 0)
    {
      return false;
    }

    if (!int.TryParse(sizeVariable!.Value, out var actualSize))
    {
      logger.LogWarning("Could not parse _size value: {value}", sizeVariable.Value);
      return false;
    }

    var itemsArrayResponse = await proxy.GetVariablesAsync(itemsVariable!.VariablesReference!.Value, cancellationToken);

    if (itemsArrayResponse?.Body?.Variables == null)
    {
      logger.LogWarning("Failed to get _items array variables");
      return false;
    }

    var actualItems = itemsArrayResponse.Body.Variables
      .Where(v => v.Name.StartsWith('[') && v.Name.EndsWith(']'))
      .OrderBy(v => ParseArrayIndex(v.Name))
      .Take(actualSize)
      .ToList();

    logger.LogInformation(
      "Simplified List: showing {actual} items (out of {capacity} capacity)",
      actualItems.Count,
      itemsArrayResponse.Body.Variables.Count);

    response.Body.Variables = actualItems;
    return true;
  }

  private static int ParseArrayIndex(string name)
  {
    var trimmed = name.Trim('[', ']');
    return int.TryParse(trimmed, out var index) ? index : -1;
  }
}