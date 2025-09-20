using EasyDotnet.MTP.RPC.Models;

using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Response;

public sealed record DiscoveryResponse(
    [property: JsonProperty("changes")]
  TestNodeUpdate[] Changes);