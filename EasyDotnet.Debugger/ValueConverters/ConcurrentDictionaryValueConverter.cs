using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class ConcurrentDictionaryValueConverter(ILogger<ConcurrentDictionaryValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "ConcurrentDictionary";

  public override bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && ConcurrentDictionaryRegex().IsMatch(val.Type);

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

    try
    {
      var filtered = variables
        .Where(v => v.Name == "Keys" || v.Name == "Values")
        .ToList();

      if (filtered.Count == 0)
      {
        LogFailure("No Keys or Values properties found", id);
        return response;
      }

      response.Body!.Variables = filtered;

      Logger.LogDebug(
        "[ConcurrentDictionary] Filtered to {Count} properties (Keys and Values)",
        filtered.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error filtering ConcurrentDictionary: {ex.Message}", id);
      return response;
    }
  }

  [GeneratedRegex(@"^System\.Collections\.Concurrent\.ConcurrentDictionary<.*>$", RegexOptions.Compiled)]
  private static partial Regex ConcurrentDictionaryRegex();
}