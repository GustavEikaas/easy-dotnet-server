namespace EasyDotnet.BuildServer.Contracts;

public record BuildServerDiagnosticsResponse(
    string RuntimeVersion,
    int RuntimeVersionMajor,
    string MsBuildVersion,
    string MsBuildPath);

public record PropertyCacheDiagnosticsResponse(
    long Evaluations,
    long MemoryHits,
    long DiskHits,
    string DiskRoot);