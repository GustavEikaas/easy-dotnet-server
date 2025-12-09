using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class CancellationTokenValueConverter(ILogger<CancellationTokenValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "CancellationToken";

  public override bool CanConvert(Variable val) =>
      !string.IsNullOrEmpty(val.Type)
      && CancellationTokenRegex().IsMatch(val.Type);

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

    ValueConverterHelpers.TryGetVariable(variables, "_source", out var sourceVar);
    if (sourceVar != null)
    {
      sourceVar.Name = "Source";
    }

    response.Body!.Variables = [.. response.Body.Variables.Where(x => !FilteredProperties.Contains(x.Name))];

    return response;
  }

  private static readonly string[] FilteredProperties = ["WaitHandle", "Static members"];

  [GeneratedRegex(@"^System\.Threading\.CancellationToken$", RegexOptions.Compiled)]
  private static partial Regex CancellationTokenRegex();
}