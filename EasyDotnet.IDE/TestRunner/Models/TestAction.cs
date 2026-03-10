using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EasyDotnet.IDE.TestRunner.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum TestAction
{
  Run,
  Debug,
  GoToSource,
  PeekResults,
  Invalidate,
  GetBuildErrors
}