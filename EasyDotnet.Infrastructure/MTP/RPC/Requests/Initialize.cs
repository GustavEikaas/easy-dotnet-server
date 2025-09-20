using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Requests;

public sealed record InitializeRequest(
  [property:JsonProperty("processId")]
  int ProcessId,

  [property:JsonProperty("clientInfo")]
  ClientInfo ClientInfo,

  [property:JsonProperty("capabilities")]
  ClientCapabilities Capabilities);

public sealed record ClientCapabilities(
  [property: JsonProperty("testing")]
  ClientTestingCapabilities Testing);

public sealed record ClientTestingCapabilities(
  [property: JsonProperty("debuggerProvider")]
  bool DebuggerProvider);

public sealed record ClientInfo(
  [property:JsonProperty("name")]
  string Name,

  [property:JsonProperty("version")]
  string Version = "1.0.0");