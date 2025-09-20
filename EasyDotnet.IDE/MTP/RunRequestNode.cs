using Newtonsoft.Json;

namespace EasyDotnet.MTP;

public sealed record RunRequestNode
(
  [property: JsonProperty("uid")]
  string Uid,

  [property: JsonProperty("display-name")]
  string DisplayName
);