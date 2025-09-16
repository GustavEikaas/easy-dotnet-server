using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EasyDotnet.Services.NetCoreDbg.ValueConverters;

public class GuidValueConverter : IValueConverter
{
  public bool SatisfiesType(string? typeName)
      => typeName != null && typeName.StartsWith("System.Guid");

  public async Task<JsonObject> ConvertValueAsync(
      Func<int> nextSequence,
      NetcoreDbgClient client,
      JsonObject variable,
      CancellationToken cancellationToken)
  {
    var varName = variable["name"]?.GetValue<string>();
    if (string.IsNullOrEmpty(varName))
    {
      return variable;
    }

    var evalReq = new JsonObject
    {
      ["seq"] = nextSequence(),
      ["type"] = "request",
      ["command"] = "evaluate",
      ["arguments"] = new JsonObject
      {
        ["expression"] = $"{varName}.ToString()",
        ["context"] = "hover"
      }
    };

    var evalResStr = await client.SendRequestAsync(evalReq, cancellationToken);
    var evalRes = JsonNode.Parse(evalResStr)?["body"];
    var result = evalRes?["result"]?.GetValue<string>();

    if (result != null)
    {
      var clone = new JsonObject
      {
        ["name"] = variable["name"]?.GetValue<string>(),
        ["rawValue"] = variable["value"]?.GetValue<string>(),
        ["value"] = result,
        ["type"] = "string",
        ["variablesReference"] = variable["variablesReference"]?.GetValue<int>() ?? 0
      };

      return clone;
    }

    return variable;
  }
}