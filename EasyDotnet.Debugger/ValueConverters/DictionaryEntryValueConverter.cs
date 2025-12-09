using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class DictionaryEntryValueConverter(ILogger<DictionaryEntryValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "DictionaryEntry";

  public override bool CanConvert(Variable val) => !string.IsNullOrEmpty(val.Type)
      && DictionaryEntryRegex().IsMatch(val.Type);

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
      var filteredVars = new List<Variable>();

      if (ValueConverterHelpers.TryGetVariable(variables, "key", out var keyVar))
      {
        var keyRenamed = new Variable
        {
          Name = "Key",
          Value = keyVar.Value,
          Type = keyVar.Type,
          VariablesReference = keyVar.VariablesReference ?? 0,
          EvaluateName = keyVar.EvaluateName,
          NamedVariables = keyVar.NamedVariables,
        };
        filteredVars.Add(keyRenamed);
      }

      if (ValueConverterHelpers.TryGetVariable(variables, "value", out var valueVar))
      {
        var valueRenamed = new Variable
        {
          Name = "Value",
          Value = valueVar.Value,
          Type = valueVar.Type,
          VariablesReference = valueVar.VariablesReference ?? 0,
          EvaluateName = valueVar.EvaluateName,
          NamedVariables = valueVar.NamedVariables,
        };
        filteredVars.Add(valueRenamed);
      }

      response.Body!.Variables = filteredVars;

      Logger.LogDebug(
        "[DictionaryEntry] Filtered to Key and Value only (removed {FilteredCount} fields)",
        variables.Count - filteredVars.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error filtering dictionary entry:  {ex.Message}", id);
      return response;
    }
  }

  [GeneratedRegex(@"^System\.Collections\.Generic\.Dictionary<.*, .*>\.Entry$", RegexOptions.Compiled)]
  private static partial Regex DictionaryEntryRegex();
}
