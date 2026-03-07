using Newtonsoft.Json;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Requests;

public sealed record DiscoveryRequest(
    [property:JsonProperty("runId")]
  Guid RunId);