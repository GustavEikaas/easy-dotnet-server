using EasyDotnet.IntegrationTests.Initialize;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace EasyDotnet.IntegrationTests.Utils;

public static class RpcTestServerInstantiator
{
  /// <summary>
  /// Creates and starts a new JSON-RPC server using an in-memory full-duplex stream pair, without sending an initial <c>initialize</c> request.
  /// </summary>
  /// <returns>
  /// A <see cref="JsonRpc"/> instance representing the server, ready to receive and process RPC calls.
  /// </returns>
  /// <remarks>
  /// This utility is intended for test scenarios where you want to manually control the server lifecycle and
  /// optionally send an <c>initialize</c> or other setup RPC call separately.
  /// The returned server is fully configured and listening, but not pre-initialized.
  /// </remarks>
  public static JsonRpc GetUninitializedStreamServer()
  {
    var (stream1, stream2) = FullDuplexStream.CreatePair();
    var server = JsonRpcServerBuilder.Build(stream1, stream2, DiModules.BuildServiceProvider);
    server.StartListening();
    return server;
  }


  /// <summary>
  /// Creates, starts, and initializes a JSON-RPC server using an in-memory full-duplex stream,
  /// sending a default <c>initialize</c> request to prepare the server for handling RPC calls.
  /// </summary>
  /// <returns>
  /// A fully initialized <see cref="JsonRpc"/> server instance, ready to receive further RPC requests.
  /// </returns>
  /// <remarks>
  /// This method is intended for integration testing scenarios where a fresh, pre-initialized server is needed.
  /// The <c>initialize</c> request uses a default <see cref="TestInitializeRequest"/> payload.
  /// </remarks>
  /// <example>
  /// <code>
  /// using var server = await TestHelpers.GetInitializedStreamServer();
  /// var result = await server.InvokeWithParameterObjectAsync&lt;MyResponse&gt;("myMethod", myParams);
  /// </code>
  /// </example>
  /// <para>
  /// <b>Important:</b> The returned <see cref="JsonRpc"/> instance is not automatically disposed.
  /// The caller is responsible for disposing it to release associated resources.
  /// </para>
  public static async Task<JsonRpc> GetInitializedStreamServer()
  {
    var server = GetUninitializedStreamServer();
    await server.InvokeWithParameterObjectAsync<TestInitializeResponse>("initialize", new List<TestInitializeRequest>() { new(new TestClientInfo("test", "2.0.0"), new TestProjectInfo(Path.GetTempPath())) });
    return server;
  }

  /// <summary>
  /// Sends a single JSON-RPC request to a test server after initializing it, and returns the typed response.
  /// </summary>
  /// <typeparam name="T">The expected return type from the RPC method.</typeparam>
  /// <param name="targetName">The name of the RPC method to invoke after initialization.</param>
  /// <param name="parameters">
  /// The parameter object to send with the RPC method. It should match the method’s expected parameter shape.
  /// </param>
  /// <returns>A task representing the asynchronous operation, with a result of type <typeparamref name="T"/>.</returns>
  /// <remarks>
  /// This method first performs an <c>initialize</c> RPC call using a default test request,
  /// ensuring the server is in a ready state before invoking the specified <paramref name="targetName"/> method.
  /// It is suitable for isolated, integration-style tests where each RPC call is made against a fresh server instance.
  /// </remarks>
  /// <exception cref="StreamJsonRpc.RemoteInvocationException">
  /// Thrown if the server fails to find or execute the specified method, or if the initialize request fails.
  /// </exception>
  public static async Task<T> InitializedOneShotRequest<T>(string targetName, object? parameters)
  {
    using var server = await GetInitializedStreamServer();
    return parameters is not null
      ? await server.InvokeWithParameterObjectAsync<T>(targetName, parameters)
      : await server.InvokeAsync<T>(targetName);
  }

}