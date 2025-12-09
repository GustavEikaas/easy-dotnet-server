using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class ReadOnlyCollectionValueConverter(ILogger<ReadOnlyCollectionValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "ReadOnlyCollection";

  public override bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && ReadOnlyCollectionRegex().IsMatch(val.Type);

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

    if (!ValueConverterHelpers.TryGetVariable(variables, "list", out var listVar) ||
        listVar.VariablesReference is null or 0)
    {
      LogFailure("Missing list field or invalid reference", id);
      return response;
    }

    var listResponse = await proxy.GetVariablesAsync(
      listVar.VariablesReference.Value,
      cancellationToken);

    if (!ValidateResponse(listResponse, listVar.VariablesReference.Value, out var listVariables))
    {
      LogFailure("Failed to retrieve list contents", id);
      return response;
    }

    try
    {
      var items = listVariables
        .Where(v => v.Name.StartsWith('[') && v.Name.EndsWith(']'))
        .Select(v => new { Variable = v, Index = ParseArrayIndex(v.Name) })
        .Where(x => x.Index >= 0)
        .OrderBy(x => x.Index)
        .Select(x => x.Variable)
        .ToList();

      response.Body!.Variables = items;

      Logger.LogDebug(
        "[ReadOnlyCollection] Unwrapped to {ItemCount} items",
        items.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error unwrapping ReadOnlyCollection: {ex.Message}", id);
      return response;
    }
  }

  private static int ParseArrayIndex(string name)
  {
    var trimmed = name.Trim('[', ']');
    return int.TryParse(trimmed, out var index) ? index : -1;
  }

  [GeneratedRegex(@"^System\.Collections\.ObjectModel\.ReadOnlyCollection<.*>$", RegexOptions.Compiled)]
  private static partial Regex ReadOnlyCollectionRegex();
}