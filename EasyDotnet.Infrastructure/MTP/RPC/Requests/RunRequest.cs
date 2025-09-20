using System;

using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Requests;

public sealed record RunRequest(
  [property:JsonProperty("tests")]
  RunRequestNode[]? TestCases,
  [property:JsonProperty("runId")]
  Guid RunId);