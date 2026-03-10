using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC;

public sealed class MtpClientFactory(
    ILogger<MtpClient> clientLogger,
    ILoggerFactory loggerFactory)
{
  private static SourceLevels ResolveTraceLevel(ILoggerFactory factory)
  {
    var probe = factory.CreateLogger("TraceLevel");
    if (probe.IsEnabled(LogLevel.Trace)) return SourceLevels.Verbose;
    if (probe.IsEnabled(LogLevel.Debug)) return SourceLevels.Information;
    if (probe.IsEnabled(LogLevel.Warning)) return SourceLevels.Warning;
    if (probe.IsEnabled(LogLevel.Error)) return SourceLevels.Error;
    return SourceLevels.Critical;
  }

  public Task<MtpClient> CreateAsync(string testExePath, CancellationToken ct = default)
  {
    var server = new MtpServer();
    return MtpClient.CreateAsync(testExePath, clientLogger, server, ResolveTraceLevel(loggerFactory), ct);
  }
}