
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public abstract class ValueConverterBase(ILogger logger) : IValueConverter
{
  protected ILogger Logger { get; } = logger;
  protected abstract string ConverterName { get; }

  public abstract bool CanConvert(Variable val);

  public abstract Task<VariablesResponse> TryConvertAsync(
    int id,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken);

  /// <summary>
  /// Helper to log conversion failures consistently
  /// </summary>
  protected void LogFailure(string reason, int variablesReference) => ValueConverterHelpers.LogConversionFailure(Logger, ConverterName, reason, variablesReference);

  /// <summary>
  /// Validates that the response has variables
  /// </summary>
  protected bool ValidateResponse(VariablesResponse? response, int id, out List<Variable> variables)
  {
    if (response?.Body?.Variables is null || response.Body.Variables.Count == 0)
    {
      if (response == null)
      {
        LogFailure("Response is null", id);
      }
      else
      {
        LogFailure("Response has no variables", id);
      }

      variables = [];
      return false;
    }

    variables = [.. response.Body.Variables];
    return true;
  }
}