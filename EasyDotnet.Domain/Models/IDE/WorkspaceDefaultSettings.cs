using System.Text.Json.Serialization;

namespace EasyDotnet.Domain.Models.IDE;

public sealed record WorkspaceSettingsDocumentV1(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("defaults")] WorkspaceDefaultSettings Defaults
)
{
  public const int CurrentVersion = 1;
}

public sealed record WorkspaceDefaultSettings(
    WorkspaceProjectReference? DefaultBuild,
    WorkspaceProjectReference? DefaultDebug,
    WorkspaceProjectReference? DefaultRun,
    WorkspaceProjectReference? DefaultTest,
    WorkspaceProjectReference? DefaultView
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceProjectType
{
  Solution = 0,
  Project = 1
}

public sealed record WorkspaceProjectReference(
    WorkspaceProjectType Type,
    string Project,
    string? TargetFramework
);