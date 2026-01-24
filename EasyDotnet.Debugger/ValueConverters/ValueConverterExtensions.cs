using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.Debugger.ValueConverters;

public static class ValueConverterExtensions
{
  /// <summary>
  /// Mutates the response body to contain only the computed result
  /// </summary>
  public static void AssignComputedResult(this VariablesResponseBody body, string value) => body.Variables =
    [
      new Variable
      {
        Value = value,
        Name = "Result",
        Type = "Computed",
        EvaluateName = "Value",
        VariablesReference = 0,
      }
    ];
}