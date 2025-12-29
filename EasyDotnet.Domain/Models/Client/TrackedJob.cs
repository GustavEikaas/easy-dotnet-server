namespace EasyDotnet.Domain.Models.Client;

public sealed record TrackedJob(Guid JobId, RunCommand Command);