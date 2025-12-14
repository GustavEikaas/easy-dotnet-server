using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class CancellationTokenSourceValueConverter(ILogger<CancellationTokenSourceValueConverter> logger)
    : ValueConverterBase(logger)
{
  protected override string ConverterName => "CancellationTokenSource";

  public override bool CanConvert(Variable val) =>
      !string.IsNullOrEmpty(val.Type)
      && CancellationTokenSourceRegex().IsMatch(val.Type);

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

    foreach (var varField in variables)
    {
      if (varField.Name == "_state")
        varField.Name = "State";
      else if (varField.Name == "_disposed")
        varField.Name = "Disposed";
      else if (varField.Name == "_timer")
        varField.Name = "Timer";
    }

    response.Body!.Variables = [.. variables.Where(v => !FilteredProperties.Contains(v.Name) && !(v.Name == "Timer" && v.Value == "null"))];

    return response;
  }

  private static readonly string[] FilteredProperties = ["_registrations", "_kernelEvent", "WaitHandle", "Static members"];

  [GeneratedRegex(@"^System\.Threading\.CancellationTokenSource$", RegexOptions.Compiled)]
  private static partial Regex CancellationTokenSourceRegex();
}