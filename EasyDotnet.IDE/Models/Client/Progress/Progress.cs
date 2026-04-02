namespace EasyDotnet.IDE.Models.Client.Progress;

public sealed record ProgressParams(string Token, ProgressValue Value);

public sealed record ProgressValue(
    string Kind,
    string? Title = null,
    string? Message = null,
    int? Percentage = null,
    bool? Cancellable = null
);