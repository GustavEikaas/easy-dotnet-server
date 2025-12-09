using System.Globalization;
using System.Text.Json;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.ValueConverters;

public static class ValueConverterHelpers
{
  public static Dictionary<string, string> BuildFieldLookup(List<Variable> variables) => variables.ToDictionary(
      v => v.Name,
      v => v.Value,
      StringComparer.Ordinal);

  /// <summary>
  /// Creates a new VariablesResponse with computed result, preserving all response metadata
  /// </summary>
  public static VariablesResponse CloneWithComputedResult(VariablesResponse original, string computedValue) => new()
  {
    Seq = original.Seq,
    Type = original.Type,
    RequestSeq = original.RequestSeq,
    Success = original.Success,
    Command = original.Command,
    Message = original.Message,
    ExtraProperties = original.ExtraProperties != null
        ? new Dictionary<string, JsonElement>(original.ExtraProperties)
        : [],

    Body = new VariablesResponseBody
    {
      Variables =
        [
          new Variable
          {
            Name = "Result",
            Value = computedValue,
            Type = "Computed",
            EvaluateName = "Value",
            VariablesReference = 0
          }
        ]
    }
  };

  /// <summary>
  /// Mutates the existing response to contain only the computed result
  /// More efficient than cloning but modifies the original
  /// </summary>
  public static void AssignComputedResult(VariablesResponse response, string computedValue)
  {
    response.Body ??= new VariablesResponseBody
    {
      Variables = []
    };

    response.Body.Variables =
    [
      new Variable
      {
        Name = "Result",
        Value = computedValue,
        Type = "Computed",
        EvaluateName = "Value",
        VariablesReference = 0
      }
    ];
  }

  public static bool TryGetInt(Dictionary<string, string> lookup, string fieldName, out int value)
  {
    if (lookup.TryGetValue(fieldName, out var strValue) &&
        int.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
    {
      return true;
    }

    value = 0;
    return false;
  }

  public static bool TryGetShort(Dictionary<string, string> lookup, string fieldName, out short value)
  {
    if (lookup.TryGetValue(fieldName, out var strValue) &&
        short.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
    {
      return true;
    }

    value = 0;
    return false;
  }

  public static bool TryGetByte(Dictionary<string, string> lookup, string fieldName, out byte value)
  {
    if (lookup.TryGetValue(fieldName, out var strValue) &&
        byte.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
    {
      return true;
    }

    value = 0;
    return false;
  }

  public static bool TryGetString(Dictionary<string, string> lookup, string fieldName, out string value) => lookup.TryGetValue(fieldName, out value!);

  public static bool TryGetEnum<T>(Dictionary<string, string> lookup, string fieldName, out T value)
      where T : struct, Enum
  {
    if (lookup.TryGetValue(fieldName, out var strValue) &&
        Enum.TryParse(strValue, out value))
    {
      return true;
    }

    value = default;
    return false;
  }

  public static bool TryGetUInt(Dictionary<string, string> lookup, string name, out uint value)
  {
    value = 0;
    return lookup.TryGetValue(name, out var variable) && uint.TryParse(variable, out value);
  }

  public static bool TryGetVariable(List<Variable> variables, string name, out Variable variable)
  {
    variable = variables.FirstOrDefault(v => v.Name == name)!;
    return variable != null;
  }

  public static void LogConversionFailure(
    ILogger logger,
    string converterName,
    string reason,
    int variablesReference) => logger.LogWarning(
      "[{Converter}] Value conversion failed for reference {Reference}: {Reason}",
      converterName,
      variablesReference,
      reason);
}
