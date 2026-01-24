namespace EasyDotnet.Domain.Models.Client;

public sealed record SetBreakpointRequest(string Path, int LineNumber);