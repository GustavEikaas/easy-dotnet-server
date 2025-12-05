using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class HashSetValueConverter(ILogger<HashSetValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "HashSet";

  public override bool CanConvert(Variable val)
      => val.Type.StartsWith("System.Collections.Generic.HashSet<");

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
      var flattened = await FlattenHashSetEntriesAsync(
        entries,
        proxy,
        cancellationToken);

      response.Body!.Variables = flattened;
      Logger.LogDebug("[HashSet] Successfully flattened {Count} entries", flattened.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error flattening HashSet entries: {ex.Message}", id);
      return response;
    }
  }

  private async Task<List<Variable>> FlattenHashSetEntriesAsync(
     List<Variable> entries,
     IDebuggerProxy proxy,
     CancellationToken cancellationToken)
  {
    var activeTasks = entries
       .Where(e => e.VariablesReference is not null and not 0)
       .Select(async entry =>
       {
         try
         {
           // Get the Entry struct fields
           var entryResponse = await proxy.GetVariablesAsync(
             entry.VariablesReference!.Value,
             cancellationToken);

           if (entryResponse?.Body?.Variables is null || entryResponse.Body.Variables.Count == 0)
           {
             return null; // Skip empty entries
           }

           var entryFields = entryResponse.Body.Variables;
           var entryLookup = ValueConverterHelpers.BuildFieldLookup(entryFields);

           // Check if this entry is active by looking at the HashCode field
           // HashCode == -1 means the entry is unused/deleted
           // HashCode >= 0 means the entry is in use
           if (!ValueConverterHelpers.TryGetInt(entryLookup, "HashCode", out var hashCode))
           {
             Logger.LogDebug("[HashSet] Entry missing 'HashCode' field, skipping");
             return null;
           }

           if (hashCode == -1)
           {
             Logger.LogDebug("[HashSet] Entry is unused (HashCode=-1), skipping");
             return null; // This entry is not in use
           }

           // Extract the "Value" field from the Entry struct
           if (!ValueConverterHelpers.TryGetVariable(entryFields, "Value", out var valueVar))
           {
             Logger.LogWarning("[HashSet] Active entry missing 'Value' field");
             return null;
           }

           Logger.LogDebug("[HashSet] Found active entry: HashCode={HashCode}, Value={Value}",
             hashCode, valueVar.Value);

           return valueVar;
         }
         catch (Exception ex)
         {
           Logger.LogWarning(ex, "[HashSet] Failed to process entry");
           return null;
         }
       });

    var results = await Task.WhenAll(activeTasks);

    // Filter out nulls and assign indices
    var activeEntries = results
      .Where(v => v != null)
      .Select((v, idx) => new Variable
      {
        Name = $"[{idx}]",
        Value = v!.Value,
        Type = v.Type,
        EvaluateName = v.EvaluateName,
        VariablesReference = v.VariablesReference
      })
      .ToList();

    Logger.LogDebug("[HashSet] Filtered to {Count} active entries", activeEntries.Count);

    return activeEntries;
  }
}