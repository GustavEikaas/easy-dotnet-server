using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class ReadOnlyDictionaryValueConverter(ILogger<ReadOnlyDictionaryValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "ReadOnlyDictionary";

  public override bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && ReadOnlyDictionaryRegex().IsMatch(val.Type);

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
    if (ValueConverterHelpers.TryGetInt(lookup, "Count", out var count) && count == 0)
    {
      response.Body!.Variables = [ValueConverterHelpers.CreateEmptyListVariable()];
      return response;
    }

    if (ValueConverterHelpers.TryGetVariable(variables, "m_dictionary", out var dict))
    {
      dict.Name = "Dictionary";
      response.Body!.Variables = [dict];
      return response;
    }

    return response;
  }

  [GeneratedRegex(@"^System\.Collections\.ObjectModel\.ReadOnlyDictionary<.*, .*>$", RegexOptions.Compiled)]
  private static partial Regex ReadOnlyDictionaryRegex();
}
