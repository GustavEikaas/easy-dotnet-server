using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Models;
using Newtonsoft.Json;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Response;

public sealed record DiscoveryResponse(
    [property: JsonProperty("changes")]
  TestNodeUpdate[] Changes);