namespace EasyDotnet.BuildServer.Contracts;

public record BuildServerDiagnosticsResponse(
    string RuntimeVersion,
    int RuntimeVersionMajor,
    string MsBuildVersion,
    string MsBuildPath);