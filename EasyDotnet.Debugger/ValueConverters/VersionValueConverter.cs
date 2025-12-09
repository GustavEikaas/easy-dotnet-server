using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class VersionValueConverter(ILogger<VersionValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "Version";

  public override bool CanConvert(Variable val) =>
      !string.IsNullOrEmpty(val.Type)
      && VersionRegex().IsMatch(val.Type);

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

    ValueConverterHelpers.TryGetInt(lookup, "_Major", out var major);
    ValueConverterHelpers.TryGetInt(lookup, "_Minor", out var minor);
    ValueConverterHelpers.TryGetInt(lookup, "_Build", out var build);
    ValueConverterHelpers.TryGetInt(lookup, "_Revision", out var revision);

    var versionString = build < 0 ? $"{major}.{minor}" : revision < 0 ? $"{major}.{minor}.{build}" : $"{major}.{minor}.{build}.{revision}";
    response.Body!.AssignComputedResult(versionString);

    return response;
  }

  [GeneratedRegex(@"^System\.Version$", RegexOptions.Compiled)]
  private static partial Regex VersionRegex();
}
