using System.IO.Pipes;
using EasyDotnet.Aspire;
using EasyDotnet.Aspire.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

var pipeName = ParsePipe(args);
if (pipeName is null)
{
  Console.Error.WriteLine("No --pipe passed");
  return 1;
}

// Logs are buffered in a ring sink (pulled by the IDE via _server/logdump) and mirrored to stderr.
var ringState = new RingLogState(ParseLogLevel(args));
using var loggerFactory = LoggerFactory.Create(b =>
{
  b.ClearProviders();
  b.SetMinimumLevel(LogLevel.Trace); // RingLogState does the dynamic filtering
  b.AddProvider(new RingLoggerProvider(ringState));
});
var logger = loggerFactory.CreateLogger("EasyDotnet.Aspire");

var pipeServer = new NamedPipeServerStream(
    pipeName,
    PipeDirection.InOut,
    maxNumberOfServerInstances: 1,
    transmissionMode: PipeTransmissionMode.Byte,
    options: PipeOptions.Asynchronous);

logger.LogInformation("Waiting for IDE connection on pipe {PipeName}", pipeName);
await pipeServer.WaitForConnectionAsync();
logger.LogInformation("IDE connected");

var formatter = new JsonMessageFormatter
{
  JsonSerializer = { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } }
};
var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipeServer, pipeServer, formatter));

var ide = new IdeCallback(rpc);
await using var server = new AspireServer(ide, loggerFactory);
rpc.AddLocalRpcTarget(server);
rpc.AddLocalRpcTarget(new ServerLogHandler(ringState));

rpc.StartListening();
logger.LogInformation("Aspire host ready");
await rpc.Completion;
logger.LogInformation("IDE disconnected; shutting down");
return 0;

static string? ParsePipe(string[] args)
{
  for (var i = 0; i < args.Length; i++)
  {
    if (args[i].Equals("--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
    {
      return args[i + 1];
    }
  }
  return null;
}

static LogLevel ParseLogLevel(string[] args)
{
  foreach (var arg in args)
  {
    if (arg.StartsWith("--log-level=", StringComparison.OrdinalIgnoreCase))
    {
      return RingLogState.Parse(arg["--log-level=".Length..]);
    }
  }
  return LogLevel.Information;
}