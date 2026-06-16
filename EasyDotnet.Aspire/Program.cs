using System.IO.Pipes;
using EasyDotnet.Aspire;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

var pipeName = ParsePipe(args);
if (pipeName is null)
{
  Console.Error.WriteLine("No --pipe passed");
  return 1;
}

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
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