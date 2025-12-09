using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public class StopwatchValueConverter(ILogger<StopwatchValueConverter> logger)
    : ValueConverterBase(logger)
{
  protected override string ConverterName => "Stopwatch";

  public override bool CanConvert(Variable val) =>
      val.Type == "System.Diagnostics.Stopwatch";

  public override async Task<VariablesResponse> TryConvertAsync(
      int id,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var response = await proxy.GetVariablesAsync(id, cancellationToken);

    if (response == null)
    {
      LogFailure("Proxy returned null response", id);
      throw new InvalidOperationException(
          $"Failed to get variables for reference {id}");
    }

    if (!ValidateResponse(response, id, out var variables))
      return response;

    ValueConverterHelpers.TryGetVariable(variables, "DebuggerDisplay", out var dbg);

    if (dbg is null)
    {
      LogFailure("DebuggerDisplay not found on Stopwatch", id);
      return response;
    }

    response!.Body!.Variables = [dbg];
    return response;
  }
}