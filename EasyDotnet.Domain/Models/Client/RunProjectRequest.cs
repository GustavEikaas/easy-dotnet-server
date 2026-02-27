using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.Domain.Models.Client;

public record RunProjectRequest(
    DotnetProject Project,
    LaunchProfile.LaunchProfile? LaunchProfile,
    string[]? AdditionalArguments,
    Dictionary<string, string>? EnvironmentVariables
);