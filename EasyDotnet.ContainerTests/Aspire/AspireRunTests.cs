using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Aspire;

/// <summary>
/// End-to-end coverage of the Aspire DCP integration through <c>workspace/run</c>:
///   1. An AppHost project (IsAspireHost) is routed to the Aspire host instead of a normal run,
///      and is launched with the <c>DEBUG_SESSION_*</c> environment that points DCP at the
///      in-process DCP server (proves the Aspire host spawned, generated creds, and started Kestrel).
///   2. The injected certificate is valid base64 and the advertised /info document negotiates the
///      baseline protocol + project launch configuration.
/// </summary>
public abstract class AspireRunTests<TContainer> : AspireRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_AspireHost_LaunchesAppHostWithDebugSessionEnvironment()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("MyAppHost", p => p.AsAspireHost())
      .Build();
    await InitializeWorkspaceAsync(ws);

    // Single runnable project → auto-selected; IsAspireHost routes it through the DCP flow.
    await RunAsync();
    var job = await ReceiveRunCommandAsync();

    var env = job.Command.EnvironmentVariables;
    Assert.Contains("DEBUG_SESSION_PORT", env.Keys);
    Assert.Contains("DEBUG_SESSION_TOKEN", env.Keys);
    Assert.Contains("DEBUG_SESSION_SERVER_CERTIFICATE", env.Keys);
    Assert.Contains("DEBUG_SESSION_INFO", env.Keys);

    // host:port form (e.g. "localhost:36593"), required by Aspire's dashboard env handler.
    var sessionEndpoint = env["DEBUG_SESSION_PORT"].Split(':');
    Assert.Equal("localhost", sessionEndpoint[0]);
    Assert.True(sessionEndpoint.Length == 2 && int.TryParse(sessionEndpoint[1], out var port) && port > 0,
      $"DEBUG_SESSION_PORT should be 'localhost:<port>', was '{env["DEBUG_SESSION_PORT"]}'");
    Assert.NotEmpty(env["DEBUG_SESSION_TOKEN"]);

    // Cert is base64-encoded DER (matches the reference extension's x509.raw form).
    var certBytes = Convert.FromBase64String(env["DEBUG_SESSION_SERVER_CERTIFICATE"]);
    Assert.NotEmpty(certBytes);

    // /info negotiates the baseline protocol and the project launch configuration.
    Assert.Contains("2024-03-03", env["DEBUG_SESSION_INFO"]);
    Assert.Contains("project", env["DEBUG_SESSION_INFO"]);

    // The launch targets the AppHost project.
    Assert.Contains("MyAppHost", job.Command.WorkingDirectory);
  }
}

public sealed class AspireRunSdk8Linux : AspireRunTests<Sdk8LinuxContainer>;
public sealed class AspireRunSdk10Linux : AspireRunTests<Sdk10LinuxContainer>;