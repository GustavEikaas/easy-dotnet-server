namespace EasyDotnet.IDE.Models.Client.Debugger;

public sealed record SetBreakpointRequest(string Path, int LineNumber);
