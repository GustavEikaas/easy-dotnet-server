namespace EasyDotnet.Domain.Models.MsBuild.Build;

public sealed record BuildResult(bool Success, List<BuildMessageWithProject> Errors, List<BuildMessageWithProject> Warnings);