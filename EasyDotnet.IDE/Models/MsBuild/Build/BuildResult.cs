namespace EasyDotnet.IDE.Models.MsBuild.Build;

public sealed record BuildResult(bool Success, List<BuildMessageWithProject> Errors, List<BuildMessageWithProject> Warnings);
