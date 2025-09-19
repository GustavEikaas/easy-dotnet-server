using EasyDotnet.Infrastructure.Dap;
using Nerdbank.Streams;

namespace EasyDotnet.Infrastructure.Tests.Dap;

public class DebuggerProxyTests
{

  private static (System.IO.Pipelines.IDuplexPipe client, System.IO.Pipelines.IDuplexPipe server) CreateStreamPair() => FullDuplexStream.CreatePipePair();

  [Test]
  public async Task StartForwardsMessagesFromClientToDebugger()
  {
    // These streams represent the connection between the client and the proxy
    var (clientToProxy, proxyToClient) = FullDuplexStream.CreatePipePair();

    // These streams represent the connection between the proxy and the debugger
    var (debuggerToProxy, proxyToDebugger) = FullDuplexStream.CreatePipePair();

    using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    // The client's output goes to the proxy, and its input comes from the proxy.
    var client = new Client(proxyToClient.AsStream(), clientToProxy.AsStream(), null);

    // The debugger's output goes to the proxy, and its input comes from the proxy.
    var debugger = new Debugger(proxyToDebugger.AsStream(), debuggerToProxy.AsStream(), null);

    var proxy = new DebuggerProxy(client, debugger);

    var testMessage = "{\"seq\":1,\"type\":\"request\",\"command\":\"initialize\"}";

    proxy.Start(cancellationTokenSource.Token);

    // Write the message to the client's output stream.
    // The proxy will read this from client.Output and forward it to debugger.Input.
    await DapMessageWriter.WriteDapMessageAsync(testMessage, client.Output, cancellationTokenSource.Token);
    await Task.Delay(1000, cancellationTokenSource.Token);
    // Now, read the forwarded message from the debugger's input stream.
    var receivedMessage = await DapMessageReader.ReadDapMessageAsync(debugger.Input, cancellationTokenSource.Token);

    await Assert.That(receivedMessage).IsEqualTo(testMessage);

    cancellationTokenSource.Cancel();
    try
    {
      await proxy.Completion;
    }
    catch (OperationCanceledException)
    {
    }
  }

  // [Test]
  // public async Task StartAsync_ForwardsMessagesFromDebuggerToClient()
  // {
  //   var (client, server) = CreateStreamPair();
  //
  //   var debugger = new Debugger(server, client, null);
  //   var proxy = new DebuggerProxy(new Client(client, server, null), debugger);
  //
  //   // Arrange: Prepare the message to be sent from debugger to client
  //   var message = "Hello from debugger";
  //   await DapMessageWriter.WriteDapMessageAsync(message, server, CancellationToken.None);
  //
  //   // Act: Start the proxy and wait for the message to be forwarded
  //   var cancellationToken = new CancellationTokenSource().Token;
  //   await proxy.StartAsync(cancellationToken);
  //
  //   // Assert: Verify that the message was received by the client
  //   var receivedMessage = await DapMessageReader.ReadDapMessageAsync(client, cancellationToken);
  //   await Assert.That(message).IsEqualTo(receivedMessage);
  // }
  //
  // [Test]
  // public async Task StartAsync_StopsWhenCancellationTokenIsCancelled()
  // {
  //   var (client, server) = CreateStreamPair();
  //   var debugger = new Debugger(server, client, null);
  //   var proxy = new DebuggerProxy(new Client(client, server, null), debugger);
  //
  //   // Arrange: Start the proxy in a separate task
  //   var cancellationTokenSource = new CancellationTokenSource();
  //   var proxyTask = proxy.StartAsync(cancellationTokenSource.Token);
  //
  //   // Act: Cancel the cancellation token to stop the proxy
  //   cancellationTokenSource.Cancel();
  //
  //   // Assert: Verify that the proxy task has completed
  //   await Assert.ThrowsAsync<TaskCanceledException>(() => proxyTask);
  // }
}