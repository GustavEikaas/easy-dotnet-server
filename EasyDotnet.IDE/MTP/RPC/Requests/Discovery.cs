using System;

using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Requests;

public sealed record DiscoveryRequest(
    [property:JsonProperty("runId")]
  Guid RunId);