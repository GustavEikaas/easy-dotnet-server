using System.IO.Pipes;
using System.Text.Json;
using EasyDotnet.AppWrapper;
using EasyDotnet.AppWrapper.Contracts;
using Spectre.Console;
using StreamJsonRpc;

var pipeName = ParsePipe(args) ?? throw new InvalidOperationException("No --pipe argument provided.");

await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

var (rpc, handler) = await AnsiConsole.Status()
  .StartAsync("Connecting...", async ctx =>
  {
    await ConnectWithRetryAsync(pipe, TimeSpan.FromSeconds(10));
    ctx.Status = "Connected.";

    var formatter = new SystemTextJsonFormatter
    {
      JsonSerializerOptions = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      }
    };

    var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, pipe, formatter));
    var handler = new AppWrapperHandler(rpc);
    rpc.AddLocalRpcTarget(handler);
    rpc.StartListening();

    ctx.Status = "Initialized.";
    await rpc.NotifyWithParameterObjectAsync("appWrapper/initialize", new AppWrapperInitInfo(Environment.ProcessId));

    return (rpc, handler);
  });

await rpc.Completion;

handler.KillCurrentProcess();

static string? ParsePipe(string[] args)
{
  for (var i = 0; i < args.Length - 1; i++)
  {
    if (args[i].Equals("--pipe", StringComparison.OrdinalIgnoreCase))
    {
      return args[i + 1];
    }
  }
  return null;
}

static async Task ConnectWithRetryAsync(NamedPipeClientStream stream, TimeSpan timeout)
{
  using var cts = new CancellationTokenSource(timeout);
  var delayMs = 50;
  while (!cts.Token.IsCancellationRequested)
  {
    try
    {
      await stream.ConnectAsync(500, cts.Token);
      return;
    }
    catch (TimeoutException) { }
    catch (IOException) { }
    await Task.Delay(delayMs, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    delayMs = Math.Min(delayMs * 2, 500);
  }
  throw new TimeoutException($"Could not connect to IDE pipe '{stream.GetType().Name}' within {timeout.TotalSeconds}s.");
}