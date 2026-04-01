namespace EasyDotnet.AppWrapper.Contracts;

public sealed record AppExitedNotification(Guid JobId, int ExitCode);