using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public class GuidValueConverter() : IValueConverter
{

  public bool CanConvert(Variable val) => val.Type == "System.Guid";

  public async Task<VariablesResponse> TryConvertAsync(int id, IDebuggerProxy proxy, CancellationToken cancellationToken)
  {
    var variablesResponse = await proxy.GetVariablesAsync(id, cancellationToken);
    if (variablesResponse is null)
    {
      throw new Exception($"Failed to resolve variables by ID {id}");
    }

    if (variablesResponse.Body?.Variables is null)
    {
      return variablesResponse;
    }

    int GetInt(string name) =>
        int.Parse(variablesResponse.Body.Variables.First(v => v.Name == name).Value);

    short GetShort(string name) =>
        short.Parse(variablesResponse.Body.Variables.First(v => v.Name == name).Value);

    byte GetByte(string name) =>
        byte.Parse(variablesResponse.Body.Variables.First(v => v.Name == name).Value);

    var a = GetInt("_a");
    var b = GetShort("_b");
    var c = GetShort("_c");
    var d = GetByte("_d");
    var e = GetByte("_e");
    var f = GetByte("_f");
    var g = GetByte("_g");
    var h = GetByte("_h");
    var i = GetByte("_i");
    var j = GetByte("_j");
    var k = GetByte("_k");

    var guid = new Guid(a, b, c, d, e, f, g, h, i, j, k);

    variablesResponse.Body.AssignComputedResult(guid == Guid.Empty ? $"Guid.Empty {Guid.Empty}" : guid.ToString());

    return variablesResponse;
  }
}