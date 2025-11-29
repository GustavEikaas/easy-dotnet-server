using System.Text.Json;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Services;

public class ListSimplifier(ILogger<ListSimplifier> logger)
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  /// <summary>
  /// Simplifies List variables in a VariablesResponse.
  /// If the response contains a List's internal structure (_items, _size), replaces it with just the items array.
  /// </summary>
  public async Task SimplifyListVariablesInResponseAsync(
    VariablesResponse response,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken = default)
  {
    if (response.Body?.Variables == null || response.Body.Variables.Count == 0)
    {
      return;
    }

    // Check if this response is showing List internals (_items, _size, Capacity, Count, etc.)
    var itemsVariable = response.Body.Variables.FirstOrDefault(v => v.Name == "_items");
    var sizeVariable = response.Body.Variables.FirstOrDefault(v => v.Name == "_size");

    if (itemsVariable == null || sizeVariable == null)
    {
      // Not a List's internal structure, nothing to simplify
      return;
    }

    // This IS a List's internal structure - let's simplify it!
    logger?.LogInformation("Detected List internal structure, simplifying...");

    if (!itemsVariable.VariablesReference.HasValue ||
        itemsVariable.VariablesReference.Value <= 0)
    {
      logger?.LogWarning("_items has no variablesReference");
      return;
    }

    if (!int.TryParse(sizeVariable.Value, out var actualSize))
    {
      logger?.LogWarning("Could not parse _size value: {value}", sizeVariable.Value);
      return;
    }

    // Get the _items array elements
    logger?.LogDebug("Fetching _items array (reference: {ref})", itemsVariable.VariablesReference.Value);
    var itemsArrayResponse = await GetVariablesAsync(
      proxy,
      itemsVariable.VariablesReference.Value,
      cancellationToken);

    if (itemsArrayResponse?.Body?.Variables == null)
    {
      logger?.LogWarning("Failed to get _items array variables");
      return;
    }

    // Slice the array by _size (only take the first 'actualSize' elements)
    var actualItems = itemsArrayResponse.Body.Variables
      .Where(v => v.Name.StartsWith("[") && v.Name.EndsWith("]"))
      .OrderBy(v => ParseArrayIndex(v.Name))
      .Take(actualSize)
      .ToList();

    logger?.LogInformation(
      "Simplified List: showing {actual} items (out of {capacity} capacity)",
      actualItems.Count,
      itemsArrayResponse.Body.Variables.Count);

    // Replace the entire response with just the actual items
    response.Body.Variables = actualItems;
  }

  private static int ParseArrayIndex(string name)
  {
    // Parse "[0]", "[1]", etc.
    var trimmed = name.Trim('[', ']');
    return int.TryParse(trimmed, out var index) ? index : -1;
  }

  private static async Task<VariablesResponse?> GetVariablesAsync(
    IDebuggerProxy proxy,
    int variablesReference,
    CancellationToken cancellationToken)
  {
    var request = new Request
    {
      Seq = 0,
      Type = "request",
      Command = "variables",
      Arguments = JsonSerializer.SerializeToElement(new
      {
        variablesReference
      }, SerializerOptions)
    };

    var response = await proxy.RunInternalRequestAsync(request, cancellationToken);
    return response as VariablesResponse;
  }
}