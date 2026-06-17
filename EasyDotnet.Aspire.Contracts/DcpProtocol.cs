using System.Text.Json.Serialization;

namespace EasyDotnet.Aspire.Contracts;

/// <summary>
/// DCP IDE-execution protocol versions.
/// DCP appends <c>?api-version=2024-03-03</c> to every request except <c>/info</c> and auto downgrades newer AppHosts.
/// See aspire/docs/specs/IDE-execution.md.
/// </summary>
public static class DcpProtocolVersions
{
  public const string V20240303 = "2024-03-03";
}

public static class LaunchConfigurationTypes
{
  // Any .NET project 
  public const string Project = "project";
  // public const string Python = "python";
}

public static class LaunchModes
{
  public const string Debug = "Debug";
  public const string NoDebug = "NoDebug";
}

public sealed record LaunchConfiguration(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("project_path")] string? ProjectPath,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("launch_profile")] string? LaunchProfile,
    [property: JsonPropertyName("disable_launch_profile")] bool? DisableLaunchProfile);

public sealed record EnvVar(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string? Value);

/// <summary><c>PUT /run_session</c></summary>
public sealed record RunSessionPayload(
    [property: JsonPropertyName("launch_configurations")] List<LaunchConfiguration> LaunchConfigurations,
    [property: JsonPropertyName("env")] List<EnvVar>? Env,
    [property: JsonPropertyName("args")] List<string>? Args);

/// <summary><c>GET /info</c></summary>
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