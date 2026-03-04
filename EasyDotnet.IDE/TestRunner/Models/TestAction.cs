using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EasyDotnet.IDE.TestRunner.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum TestAction
{
    Run,
    Debug,
    GoToSource,
    PeekResults,  // stdout + stack trace combined — available after any completed run
    Invalidate
}
