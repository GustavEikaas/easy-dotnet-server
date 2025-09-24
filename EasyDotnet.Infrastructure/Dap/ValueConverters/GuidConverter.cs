namespace EasyDotnet.Infrastructure.Dap.ValueConverters;

public class GuidVariableConverter : IVariableConverter
{
  public bool CanConvert(InterceptableVariable variable)
      => variable.Type == "System.Guid";

  public async Task<InterceptableVariable> ConvertAsync(
      InterceptableVariable variable,
      Func<int> getNextSequence,
      Func<InternalVariablesRequest, int, CancellationToken, Task<InterceptableVariablesResponse>> resolveVariable)
  {
    if (variable.VariablesReference == 0)
    {
      return variable;
    }

    var seq = getNextSequence();

    var req = new InternalVariablesRequest
    {
      Seq = seq,
      Command = "variables",
      Type = "request",
      Arguments = new InternalVariablesArguments
      {
        VariablesReference = variable.VariablesReference
      }
    };

    var res = await resolveVariable(req, seq, CancellationToken.None);

    var guidValue = ConstructGuidFromVariables(res.Body.Variables);

    return variable with
    {
      VariablesReference = 0,
      Value = guidValue
    };

  }

  private static int GetIntValue(Dictionary<string, string?> variables, string key)
  {
    if (variables.TryGetValue(key, out var value) && int.TryParse(value, out var result))
    {
      return result;
    }

    throw new ArgumentException($"Missing or invalid value for {key}");
  }

  private static short GetShortValue(Dictionary<string, string?> variables, string key)
  {
    if (variables.TryGetValue(key, out var value) && short.TryParse(value, out var result))
    {
      return result;
    }

    throw new ArgumentException($"Missing or invalid value for {key}");
  }

  private static byte GetByteValue(Dictionary<string, string?> variables, string key)
  {
    if (variables.TryGetValue(key, out var value) && byte.TryParse(value, out var result))
    {
      return result;
    }

    throw new ArgumentException($"Missing or invalid value for {key}");
  }

  private static string ConstructGuidFromVariables(List<InterceptableVariable> variables)
  {
    var dict = variables.ToDictionary(v => v.Name, v => v.Value);

    return new Guid(
        GetIntValue(dict, "_a"),
        GetShortValue(dict, "_b"),
        GetShortValue(dict, "_c"),
        GetByteValue(dict, "_d"),
        GetByteValue(dict, "_e"),
        GetByteValue(dict, "_f"),
        GetByteValue(dict, "_g"),
        GetByteValue(dict, "_h"),
        GetByteValue(dict, "_i"),
        GetByteValue(dict, "_j"),
        GetByteValue(dict, "_k")
    ).ToString();
  }

}