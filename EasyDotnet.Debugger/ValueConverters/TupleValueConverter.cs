using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public partial class TupleValueConverter() : IValueConverter
{
  public bool CanConvert(Variable val)
      => TupleRegex().IsMatch(val.Type);

  public async Task<VariablesResponse> TryConvertAsync(
      int id,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var variablesResponse = await proxy.GetVariablesAsync(id, cancellationToken) ?? throw new Exception($"Failed to resolve variables by ID {id}");
    var vars = variablesResponse.Body?.Variables;
    if (vars is null)
      return variablesResponse;

    var items = vars
        .Where(v => v.Name.StartsWith("Item"))
        .OrderBy(v => ParseIndex(v.Name))
        .ToList();

    static int ParseIndex(string name)
        => int.TryParse(name["Item".Length..], out var i) ? i : int.MaxValue;

    variablesResponse.Body = new VariablesResponseBody()
    {
      Variables = [.. vars
            .Where(v => v.Name.StartsWith("Item"))
            .OrderBy(v => ParseIndex(v.Name))
            .Select((v, idx) => new Variable
            {
              Name = $"[{idx + 1}]",
              Value = v.Value,
              Type = v.Type,
              EvaluateName = v.EvaluateName,
              VariablesReference = v.VariablesReference
            })]
    };

    return variablesResponse;
  }

  [GeneratedRegex(@"^System\.Tuple(<.*>)?$", RegexOptions.Compiled)]
  private static partial Regex TupleRegex();
}