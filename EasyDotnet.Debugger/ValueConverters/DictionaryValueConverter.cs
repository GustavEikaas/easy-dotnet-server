using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class DictionaryValueConverter(ILogger<DictionaryValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "Dictionary";

  public override bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && DictionaryRegex().IsMatch(val.Type);

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
    if (!ValueConverterHelpers.TryGetInt(lookup, "_count", out var count))
    {
      LogFailure("Missing or invalid _count field", id);
      return response;
    }

    if (count == 0)
    {
      Logger.LogDebug("[Dictionary] Dictionary is empty (count: 0)");

      response.Body!.Variables = [
        ValueConverterHelpers. TryGetVariable(variables, "Count", out var countVar)
          ? countVar
          : ValueConverterHelpers.CreateEmptyListVariable()
      ];

      return response;
    }

    if (!ValueConverterHelpers.TryGetVariable(variables, "_entries", out var entriesVar) ||
        entriesVar.VariablesReference is null or 0)
    {
      LogFailure("Missing _entries field or invalid reference", id);
      return response;
    }

    var entriesResponse = await proxy.GetVariablesAsync(
      entriesVar.VariablesReference.Value,
      cancellationToken);

    if (!ValidateResponse(entriesResponse, entriesVar.VariablesReference.Value, out var entries))
    {
      LogFailure("Failed to retrieve _entries array", id);
      return response;
    }

    try
    {
      var activeEntries = new List<Variable>();
      var entryIndex = 0;

      foreach (var entry in entries.Where(v => v.Name.StartsWith('[') && v.Name.EndsWith(']')))
      {
        if (entry.VariablesReference is null or 0)
          continue;

        var entryFieldsResponse = await proxy.GetVariablesAsync(
          entry.VariablesReference.Value,
          cancellationToken);

        if (!ValidateResponse(entryFieldsResponse, entry.VariablesReference.Value, out var entryFields))
          continue;

        var entryLookup = ValueConverterHelpers.BuildFieldLookup(entryFields);

        if (!ValueConverterHelpers.TryGetUInt(entryLookup, "hashCode", out var hashCode) || hashCode == 0)
          continue;

        var hasKey = ValueConverterHelpers.TryGetVariable(entryFields, "key", out var keyVar);
        var hasValue = ValueConverterHelpers.TryGetVariable(entryFields, "value", out var valueVar);

        var renamedEntry = new Variable
        {
          Name = $"[{entryIndex}]",
          Value = hasKey && hasValue
            ? $"{{Key = {keyVar!.Value}, Value = {valueVar!.Value}}}"
            : entry.Value,
          Type = entry.Type,
          VariablesReference = entry.VariablesReference.Value,
        };

        activeEntries.Add(renamedEntry);
        entryIndex++;
      }

      response.Body!.Variables = activeEntries;

      Logger.LogDebug(
        "[Dictionary] Filtered to {ActiveCount} entries (from capacity {TotalCount})",
        activeEntries.Count,
        entries.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error filtering dictionary entries:  {ex.Message}", id);
      return response;
    }
  }

  [GeneratedRegex(@"^System\.Collections\.Generic\.Dictionary<.*, .*>$", RegexOptions.Compiled)]
  private static partial Regex DictionaryRegex();
}