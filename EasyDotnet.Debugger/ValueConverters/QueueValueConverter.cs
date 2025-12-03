using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public class QueueValueConverter() : IValueConverter
{
  public bool CanConvert(Variable val)
      => val.Type.StartsWith("System.Collections.Generic.Queue<");

  public async Task<VariablesResponse> TryConvertAsync(
      int id,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var resp = await proxy.GetVariablesAsync(id, cancellationToken);
    if (resp?.Body?.Variables is null)
      return resp!;

    var vars = resp.Body.Variables;

    int GetInt(string name) =>
        int.Parse(vars.First(v => v.Name == name).Value);

    var arrayVar = vars.First(v => v.Name == "_array");
    if (arrayVar.VariablesReference is null || arrayVar.VariablesReference == 0)
    {
      return resp;
    }

    var arrayResp = await proxy.GetVariablesAsync(arrayVar.VariablesReference.Value, cancellationToken);
    if (arrayResp is null)
    {
      return resp;
    }
    var arr = arrayResp.Body?.Variables ?? [];

    var head = GetInt("_head");
    var size = GetInt("_size");
    var capacity = arr.Count;

    resp.Body.Variables =
        [.. Enumerable.Range(0, size)
            .Select(i =>
            {
              var index = (head + i) % capacity;
              var element = arr[index];

              return new Variable
              {
                Name = i.ToString(),
                Value = i == 0 ? $"{element.Value} (current)" : element.Value,
                Type = element.Type,
                EvaluateName = element.EvaluateName,
                VariablesReference = element.VariablesReference
              };
            })];

    return resp;
  }
}