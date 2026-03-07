using Newtonsoft.Json;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Requests;

public sealed record RunRequest(
  [property:JsonProperty("tests")]
  RunRequestNode[]? TestCases,
  [property:JsonProperty("runId")]
  Guid RunId);