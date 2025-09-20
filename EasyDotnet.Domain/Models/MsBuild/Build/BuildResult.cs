namespace EasyDotnet.Domain.Models.MsBuild.Build;

public sealed record BuildResult(bool Success, List<BuildMessage> Errors, List<BuildMessage> Warnings);
