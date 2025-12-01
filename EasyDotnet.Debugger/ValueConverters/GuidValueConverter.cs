using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.ValueConverters;

public class GuidValueConverter() : IValueConverter
{

  public bool CanConvert(VariablesResponse response)
  {
    var val = response.Body?.Variables.Find(x => x.Type == "System.Guid" && x.VariablesReference != 0);
    return val is not null;
  }

  public bool CanConvert(Variable val) => throw new NotImplementedException();

  public async Task<bool> TryConvertAsync(
      VariablesResponse response,
      IDebuggerProxy proxy,
      CancellationToken cancellationToken)
  {
    var val = response.Body?.Variables
        .Find(x => x.Type == "System.Guid" && x.VariablesReference != 0);

    if (val == null || val.VariablesReference is null or 0)
      return false;

    var fields = await proxy.GetVariablesAsync(val.VariablesReference.Value, cancellationToken);
    if (fields?.Body?.Variables is null)
      return false;

    int GetInt(string name) =>
        int.Parse(fields.Body.Variables.First(v => v.Name == name).Value);

    short GetShort(string name) =>
        short.Parse(fields.Body.Variables.First(v => v.Name == name).Value);

    byte GetByte(string name) =>
        byte.Parse(fields.Body.Variables.First(v => v.Name == name).Value);

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

    //TOOD: if all of them are 0 just return "null", possibly need a better way to tell if a var is null

    var guid = new Guid(a, b, c, d, e, f, g, h, i, j, k);

    val.VariablesReference = 0;
    val.Value = guid.ToString();

    return true;
  }

  Task<VariablesResponse> IValueConverter.TryConvertAsync(int id, IDebuggerProxy proxy, CancellationToken cancellationToken) => throw new NotImplementedException();
}