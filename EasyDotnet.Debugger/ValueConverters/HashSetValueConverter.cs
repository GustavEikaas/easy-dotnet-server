using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public class HashSetValueConverter() : IValueConverter
{
  public bool CanConvert(Variable val)
      => val.Type.StartsWith("System.Collections.Generic.HashSet<");

  public async Task<VariablesResponse> TryConvertAsync(
      int id,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var resp = await proxy.GetVariablesAsync(id, cancellationToken);
    if (resp?.Body?.Variables is null)
    {
      return resp!;
    }

    var vars = resp.Body.Variables;

    var entriesVar = vars.FirstOrDefault(v => v.Name == "_entries");
    if (entriesVar == null || entriesVar.VariablesReference is null or 0)
    {
      return resp;
    }

    var entriesResp = await proxy.GetVariablesAsync(entriesVar.VariablesReference.Value, cancellationToken);
    var entries = entriesResp?.Body?.Variables;
    if (entries == null)
    {
      return resp;
    }

    var count = int.Parse(vars.First(v => v.Name == "_count").Value);

    var flattened = await Task.WhenAll(
        entries
            .Take(count)
            .Where(e => e.VariablesReference.HasValue && e.VariablesReference.Value != 0)
            .Select(async (entry, idx) =>
            {
              var entryResp = await proxy.GetVariablesAsync(entry.VariablesReference ?? 0, cancellationToken);
              var valueVar = (entryResp?.Body?.Variables ?? []).First(v => v.Name == "Value");

              return new Variable
              {
                Name = $"[{idx}]",
                Value = valueVar.Value,
                Type = valueVar.Type,
                EvaluateName = valueVar.EvaluateName,
                VariablesReference = valueVar.VariablesReference
              };
            })
    );

    resp.Body.Variables = [.. flattened];

    return resp;
  }
}