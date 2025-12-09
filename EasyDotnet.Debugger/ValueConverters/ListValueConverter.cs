using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class ListValueConverter(ILogger<ListValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "List";

  public override bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && AnyList().IsMatch(
        val.Type);

  public override async Task<VariablesResponse> TryConvertAsync(int id, IDebuggerProxy proxy, CancellationToken cancellationToken)
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
    if (!ValueConverterHelpers.TryGetInt(lookup, "_size", out var actualSize))
    {
      LogFailure("Missing or invalid _size field", id);
      return response;
    }

    if (actualSize == 0)
    {
      Logger.LogDebug("[List] List is empty (size: 0)");

      response.Body!.Variables = [
        ValueConverterHelpers. TryGetVariable(variables, "Count", out var countVar)
        ? countVar
        : new Variable { Name = "Count", Value = "0", Type = "int", VariablesReference = 0 }
      ];

      return response;
    }

    if (!ValueConverterHelpers.TryGetVariable(variables, "_items", out var itemsVar) ||
        itemsVar.VariablesReference is null or 0)
    {
      LogFailure("Missing _items field or invalid reference", id);
      return response;
    }

    var itemsResponse = await proxy.GetVariablesAsync(
      itemsVar.VariablesReference.Value,
      cancellationToken);

    if (!ValidateResponse(itemsResponse, itemsVar.VariablesReference.Value, out var items))
    {
      LogFailure("Failed to retrieve _items array", id);
      return response;
    }

    try
    {
      var activeItems = items
        .Where(v => v.Name.StartsWith('[') && v.Name.EndsWith(']'))
        .Select(v => new { Variable = v, Index = ParseArrayIndex(v.Name) })
        .Where(x => x.Index >= 0 && x.Index < actualSize)
        .OrderBy(x => x.Index)
        .Select(x => x.Variable)
        .ToList();

      response.Body!.Variables = activeItems;

      Logger.LogDebug(
        "[List] Filtered to {ActiveCount} items (from capacity {TotalCount})",
        activeItems.Count,
        items.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error filtering list items: {ex.Message}", id);
      return response;
    }
  }

  private static int ParseArrayIndex(string name)
  {
    var trimmed = name.Trim('[', ']');
    return int.TryParse(trimmed, out var index) ? index : -1;
  }

  [GeneratedRegex(@"^System\.Collections\.Generic\.List<.*>$", RegexOptions.Compiled)]
  private static partial Regex AnyList();
}