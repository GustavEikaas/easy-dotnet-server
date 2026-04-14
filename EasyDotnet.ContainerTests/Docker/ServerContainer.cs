using System.Diagnostics;
using System.IO.Pipes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Docker;

/// <summary>
/// One container = one server process. Create per test, dispose after.
/// The container installs the server as a NuGet global tool from a local feed,
/// then runs it. Testcontainers waits for the "Named pipe server started:" log line.
/// /tmp is bind-mounted host↔container so the Unix Domain Socket is shared.
/// </summary>
public abstract class ServerContainer : IAsyncDisposable
{
  private const string NugetContainerPath = "/nuget";

  // Unique per instance so parallel containers don't share the tool install directory.
  private readonly string _toolInstallPath = $"/tmp/tool-{Guid.NewGuid():N}";
  private string ToolExe => $"{_toolInstallPath}/dotnet-easydotnet";
  public abstract int SdkMajorVersion { get; }
  private IContainer _container = null!;
  private NamedPipeClientStream _pipe = null!;

  public JsonRpc Rpc { get; private set; } = null!;

  protected abstract string Image { get; }
  protected abstract string TmpMountPath { get; }

  protected virtual ContainerBuilder ConfigureContainer(ContainerBuilder builder) => builder;

  private static (string feedPath, string version) GetNugetFeed()
  {
    var feedPath = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory, "..", "..", "..", "..",
      "EasyDotnet.ContainerTests", "nuget-feed"));

    if (!Directory.Exists(feedPath))
      throw new DirectoryNotFoundException(
        $"NuGet feed not found at '{feedPath}'. " +
        "Build EasyDotnet.ContainerTests to trigger the AfterBuild pack step.");

    var nupkg = Directory.EnumerateFiles(feedPath, "EasyDotnet.*.nupkg").FirstOrDefault()
      ?? throw new FileNotFoundException($"No EasyDotnet nupkg found in '{feedPath}'.");

    // "EasyDotnet.3.0.31.nupkg" → "3.0.31"
    var version = Path.GetFileNameWithoutExtension(nupkg)["EasyDotnet.".Length..];
    return (feedPath, version);
  }

  public async Task StartAsync(CancellationToken ct = default)
  {
    var (feedPath, version) = GetNugetFeed();

    var installAndRun =
      $"dotnet tool install EasyDotnet" +
      $" --add-source {NugetContainerPath}" +
      $" --version {version}" +
      $" --tool-path {_toolInstallPath}" +
      $" && {ToolExe}";

    var builder = new ContainerBuilder(Image)
      .WithBindMount(feedPath, NugetContainerPath, AccessMode.ReadOnly)
      .WithBindMount(TmpMountPath, TmpMountPath, AccessMode.ReadWrite)
      .WithCommand("sh", "-c", installAndRun)
      .WithOutputConsumer(Consume.RedirectStdoutAndStderrToConsole())
      .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilMessageIsLogged("Named pipe server started:"));

    builder = ConfigureContainer(builder);

    _container = builder.Build();
    await _container.StartAsync(ct);

    var (stdout, stderr) = await _container.GetLogsAsync(timestampsEnabled: false, ct: ct);
    var allLines = (stdout + "\n" + stderr).Split('\n');
    var match = allLines.FirstOrDefault(l => l.Contains("Named pipe server started:"))
      ?? throw new InvalidOperationException(
           $"Server started but pipe name line not found in logs.\nStdout:\n{stdout}\nStderr:\n{stderr}");

    var pipeName = match[(match.LastIndexOf(':') + 1)..].Trim();

    _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await _pipe.ConnectAsync(5_000, ct);

    Rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_pipe, _pipe, CreateFormatter()));
    Rpc.TraceSource.Switch.Level = SourceLevels.All;
    Rpc.TraceSource.Listeners.Clear();
    Rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());
    OnConfigureRpc(Rpc);
    Rpc.StartListening();
  }

  /// <summary>
  /// Called with the <see cref="JsonRpc"/> instance immediately before <see cref="JsonRpc.StartListening"/>.
  /// Override to register local RPC method handlers for server-initiated reverse requests
  /// (e.g. <c>promptSelection</c>, <c>runCommandManaged</c>) via
  /// <see cref="JsonRpc.AddLocalRpcMethod"/>.
  /// Alternatively, set <see cref="RpcConfigurator"/> before calling <see cref="StartAsync"/>.
  /// </summary>
  protected virtual void OnConfigureRpc(JsonRpc rpc) => RpcConfigurator?.Invoke(rpc);

  /// <summary>
  /// Optional delegate invoked by <see cref="OnConfigureRpc"/>.
  /// Set this before calling <see cref="StartAsync"/> to register reverse-request handlers
  /// without subclassing.
  /// </summary>
  public Action<JsonRpc>? RpcConfigurator { get; set; }

  private static JsonMessageFormatter CreateFormatter() => new()
  {
    JsonSerializer =
    {
      ContractResolver = new DefaultContractResolver
      {
        NamingStrategy = new CamelCaseNamingStrategy()
      }
    }
  };

  public async ValueTask DisposeAsync()
  {
    Rpc?.Dispose();
    _pipe?.Dispose();
    await _container.DisposeAsync();
  }
}