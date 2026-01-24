using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class QueueValueConverter(ILogger<QueueValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "Queue";

  public override bool CanConvert(Variable val)
      => val.Type.StartsWith("System.Collections.Generic.Queue<");

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

    if (!ValueConverterHelpers.TryGetVariable(variables, "_array", out var arrayVar) ||
        arrayVar.VariablesReference is null or 0)
    {
      LogFailure("Missing _array field or invalid reference", id);
      return response;
    }

    var lookup = ValueConverterHelpers.BuildFieldLookup(variables);
    if (!ValueConverterHelpers.TryGetInt(lookup, "_head", out var head) ||
        !ValueConverterHelpers.TryGetInt(lookup, "_size", out var size))
    {
      LogFailure("Missing or invalid _head/_size fields", id);
      return response;
    }

    var arrayResponse = await proxy.GetVariablesAsync(
      arrayVar.VariablesReference.Value,
      cancellationToken);

    if (!ValidateResponse(arrayResponse, arrayVar.VariablesReference.Value, out var arrayItems))
    {
      LogFailure("Failed to retrieve _array contents", id);
      return response;
    }

    var capacity = arrayItems.Count;

    try
    {
      response.Body!.Variables = UnwrapCircularBuffer(arrayItems, head, size, capacity);

      Logger.LogDebug(
        "[Queue] Unwrapped {Size} items from circular buffer (head={Head}, capacity={Capacity})",
        size,
        head,
        capacity);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error unwrapping Queue: {ex.Message}", id);
      return response;
    }
  }

  private List<Variable> UnwrapCircularBuffer(
     List<Variable> arrayItems,
     int head,
     int size,
     int capacity)
  {
    if (size == 0)
    {
      Logger.LogDebug("[Queue] Queue is empty");
      return [];
    }

    if (size > capacity)
    {
      Logger.LogWarning("[Queue] Size {Size} exceeds capacity {Capacity}, clamping", size, capacity);
      size = capacity;
    }

    var queueItems = new List<Variable>(size);

    for (var i = 0; i < size; i++)
    {
      var index = (head + i) % capacity;

      if (index < 0 || index >= arrayItems.Count)
      {
        Logger.LogWarning("[Queue] Calculated index {Index} out of bounds", index);
        continue;
      }

      var element = arrayItems[index];

      queueItems.Add(new Variable
      {
        Name = $"[{i}]",
        Value = i == 0 ? $"{element.Value} ← head" : element.Value,
        Type = element.Type,
        EvaluateName = element.EvaluateName,
        VariablesReference = element.VariablesReference
      });
    }

    return queueItems;
  }
}