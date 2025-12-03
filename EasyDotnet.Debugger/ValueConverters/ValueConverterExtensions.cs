using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.Debugger.ValueConverters;

public static class ValueConverterExtensions
{
  public static void AssignComputedResult(this VariablesResponseBody body, string value) => body.Variables = [
    new Variable() {
      Value = value,
      Name = "Result",
      Type = "Computed",
      EvaluateName = "Value",
      VariablesReference = 0,
    }
  ];
}
