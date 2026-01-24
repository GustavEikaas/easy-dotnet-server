using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class TupleValueConverter(ILogger<TupleValueConverter> logger) : ValueConverterBase(logger)
{
  protected override string ConverterName => "Tuple";

  public override bool CanConvert(Variable val)
      => TupleRegex().IsMatch(val.Type);

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

    try
    {
      var tupleItems = variables
        .Where(v => v.Name.StartsWith("Item", StringComparison.Ordinal))
        .Select(v => new { Variable = v, Index = ParseItemIndex(v.Name) })
        .Where(x => x.Index > 0)
        .OrderBy(x => x.Index)
        .Select(x => new Variable
        {
          Name = $"[{x.Index}]",
          Value = x.Variable.Value,
          Type = x.Variable.Type,
          EvaluateName = x.Variable.EvaluateName,
          VariablesReference = x.Variable.VariablesReference
        })
        .ToList();

      if (tupleItems.Count == 0)
      {
        LogFailure("No valid Item fields found in tuple", id);
        return response;
      }

      response.Body!.Variables = tupleItems;

      Logger.LogDebug("[Tuple] Reformatted {Count} tuple items", tupleItems.Count);

      return response;
    }
    catch (Exception ex)
    {
      LogFailure($"Error reformatting tuple items: {ex.Message}", id);
      return response;
    }
  }

  private static int ParseItemIndex(string name)
  {
    if (!name.StartsWith("Item", StringComparison.Ordinal))
      return -1;

    var indexPart = name["Item".Length..];
    return int.TryParse(indexPart, out var index) ? index : -1;
  }

  [GeneratedRegex(@"^System\.Tuple(<.*>)?$", RegexOptions.Compiled)]
  private static partial Regex TupleRegex();
}