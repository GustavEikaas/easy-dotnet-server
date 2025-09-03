using EasyDotnet.IntegrationTests.Utils;
using StreamJsonRpc;

namespace EasyDotnet.IntegrationTests.Initialize;

public sealed record TestServerInfo(string Name, string Version);

public sealed record TestInitializeResponse(TestServerInfo ServerInfo, TestServerCapabilities Capabilities);

public sealed record TestServerCapabilities(List<string> Routes, List<string> ServerSentNotifications);

public sealed record TestInitializeRequest(TestClientInfo ClientInfo, TestProjectInfo ProjectInfo);

public sealed record TestProjectInfo(string RootDir);

public sealed record TestClientInfo(string Name, string? Version);

public class InitializeTests
{
  public static readonly TestClientInfo DummyTestInfo = new("test", "2.0.0");

  [Fact]
  public async Task InitializeShouldPass()
  {
    using var server = RpcTestServerInstantiator.GetUninitializedStreamServer();

    var res = await server.InvokeWithParameterObjectAsync<TestInitializeResponse>("initialize", new List<TestInitializeRequest>() { new(new TestClientInfo("test", "2.0.0"), new TestProjectInfo(Path.GetTempPath())) });

    Assert.NotNull(res);
    Assert.NotNull(res.ServerInfo);
    Assert.False(string.IsNullOrWhiteSpace(res.ServerInfo.Name), "ServerInfo.Name should not be null or empty");
    Assert.True(
        Version.TryParse(res.ServerInfo.Version, out _),
        $"ServerInfo.Version '{res.ServerInfo.Version}' is not a valid version string"
    );
    Assert.NotNull(res.Capabilities);
    Assert.NotNull(res.Capabilities.Routes);
    Assert.NotEmpty(res.Capabilities.Routes);
    Assert.All(res.Capabilities.Routes, route =>
        Assert.False(string.IsNullOrWhiteSpace(route), "Routes must not contain null or empty values")
    );
    Assert.NotNull(res.Capabilities.ServerSentNotifications);
    Assert.NotEmpty(res.Capabilities.ServerSentNotifications);
    Assert.All(res.Capabilities.ServerSentNotifications, notification =>
        Assert.False(string.IsNullOrWhiteSpace(notification), "ServerSentNotifications must not contain null or empty values")
    );
  }

  [Fact]
  public async Task InitializeWithInvalidClientVersionShouldFail()
  {
    using var server = RpcTestServerInstantiator.GetUninitializedStreamServer();

    var ex = await Assert.ThrowsAsync<RemoteInvocationException>(async () => await server.InvokeWithParameterObjectAsync<TestInitializeResponse>("initialize", new List<TestInitializeRequest>() { new(new("test", "abc"), new TestProjectInfo(Path.GetTempPath())) }));

    Assert.Contains("Invalid client version format", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task InitializeWithInvalidDirectoryShouldFail()
  {
    using var server = RpcTestServerInstantiator.GetUninitializedStreamServer();

    var ex = await Assert.ThrowsAsync<RemoteInvocationException>(async () => await server.InvokeWithParameterObjectAsync<TestInitializeResponse>("initialize", new List<TestInitializeRequest>() { new(DummyTestInfo, new TestProjectInfo("some-path-does-not-exist")) }));

    Assert.Contains("some-path-does-not-exist", ex.Message, StringComparison.OrdinalIgnoreCase);
  }
}