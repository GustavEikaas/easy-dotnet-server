using EasyDotnet.Domain.Models.MTP;

using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Response;

public sealed record DiscoveryResponse(
    [property: JsonProperty("changes")]
  TestNodeUpdate[] Changes);