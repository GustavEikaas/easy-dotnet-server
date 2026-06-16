using System.Text.Json.Serialization;

namespace EasyDotnet.Aspire.Contracts;

/// <summary>
/// Well-known DCP IDE-execution protocol versions.
/// The MVP advertises only <see cref="V20240303"/> (baseline): DCP appends
/// <c>?api-version=2024-03-03</c> to every request except <c>/info</c> and
/// auto-downgrades newer AppHosts. See aspire/docs/specs/IDE-execution.md.
/// </summary>
public static class DcpProtocolVersions
{
  public const string V20240303 = "2024-03-03";
}

/// <summary>Launch configuration kinds the IDE endpoint understands.</summary>
public static class LaunchConfigurationTypes
{
  public const string Project = "project";
}

/// <summary>Launch modes DCP can request for a resource.</summary>
public static class LaunchModes
{
  public const string Debug = "Debug";
  public const string NoDebug = "NoDebug";
}

/// <summary>
/// A single launch configuration inside a <see cref="RunSessionPayload"/>.
/// The spike confirmed that a non-debugger <c>dotnet run</c> AppHost sends
/// <c>type=project</c> with <c>mode=NoDebug</c>.
/// </summary>
public sealed record LaunchConfiguration(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("project_path")] string? ProjectPath,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("launch_profile")] string? LaunchProfile,
    [property: JsonPropertyName("disable_launch_profile")] bool? DisableLaunchProfile);

/// <summary>An environment variable entry as modeled by DCP (<c>{name,value}</c>).</summary>
public sealed record EnvVar(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string? Value);

/// <summary>Body of a <c>PUT /run_session</c> request.</summary>
public sealed record RunSessionPayload(
    [property: JsonPropertyName("launch_configurations")] List<LaunchConfiguration> LaunchConfigurations,
    [property: JsonPropertyName("env")] List<EnvVar>? Env,
    [property: JsonPropertyName("args")] List<string>? Args);

/// <summary>Response document for <c>GET /info</c> (capability negotiation).</summary>
public sealed record IdeEndpointInfo(
    [property: JsonPropertyName("protocols_supported")] List<string> ProtocolsSupported,
    [property: JsonPropertyName("supported_launch_configurations")] List<string> SupportedLaunchConfigurations);

/// <summary>
/// Error body returned on 4xx/5xx responses (spec §"Error reporting"). DCP surfaces
/// <see cref="ErrorDetail.Message"/> in the Aspire application-host execution log.
/// </summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] ErrorDetail Error);

public sealed record ErrorDetail(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] IReadOnlyList<ErrorDetail>? Details = null);