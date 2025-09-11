using Newtonsoft.Json;

namespace EasyDotnet.MTP.RPC.Models;

public sealed record TestNodeUpdate
(
  [property: JsonProperty("node")]
  TestNode Node,

  [property: JsonProperty("parent")]
  string ParentUid);

public sealed record TestNode
(
  [property: JsonProperty("uid")]
  string Uid,

  [property: JsonProperty("display-name")]
  string DisplayName,

  [property: JsonProperty("location.namespace")]
  string? TestNamespace,

  [property: JsonProperty("location.method")]
  string TestMethod,

  [property: JsonProperty("location.type")]
  string TestType,

  [property: JsonProperty("location.file")]
  string? FilePath,

  [property: JsonProperty("location.line-start")]
  int? LineStart,

  [property: JsonProperty("location.line-end")]
  int? LineEnd,

  [property: JsonProperty("error.message")]
  string? Message,

  [property: JsonProperty("error.stacktrace")]
  string? StackTrace,

  [property: JsonProperty("time.duration-ms")]
  float? Duration,

  [property: JsonProperty("node-type")]
  string NodeType,

  [property: JsonProperty("execution-state")]
  string ExecutionState,

  [property: JsonProperty("standardOutput")]
  string? StandardOutput
);