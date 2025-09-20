using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Response;

public sealed record ServerCapabilities(
    [property: JsonProperty("testing")]
  ServerTestingCapabilities Testing);

public sealed record ServerTestingCapabilities(
    [property: JsonProperty("supportsDiscovery")]
  bool SupportsDiscovery,
    [property: JsonProperty("experimental_multiRequestSupport")]
  bool MultiRequestSupport,
    [property: JsonProperty("vsTestProvider")]
  bool VSTestProvider);

public sealed record ServerInfo(
    [property:JsonProperty("name")]
  string Name,

    [property:JsonProperty("version")]
  string Version = "1.0.0");
public sealed record InitializeResponse(
    ServerInfo ServerInfo,
    ServerCapabilities Capabilities);