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
  private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

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
    await OnBeforeStartAsync(ct);

    const int maxAttempts = 3;
    Exception? lastException = null;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
      try
      {
        await StartContainerCoreAsync(ct);
        return;
      }
      catch (Exception ex) when (IsSegfault(ex))
      {
        // Intermittent SIGSEGV (exit 139) under resource pressure — clean up and retry.
        lastException = ex;
        await TryDisposeContainerAsync();

        if (attempt == maxAttempts)
          break;

        // Back off briefly so concurrent containers have time to release resources.
        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
      }
      catch (Exception ex)
      {
        var logs = await TryGetLogsAsync(ct);
        await TryDisposeContainerAsync();

        if (IsDotnetFirstTimeUseMutexFailure(ex, logs))
        {
          lastException = ex;

          if (attempt == maxAttempts)
            break;

          await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
          continue;
        }

        throw new InvalidOperationException(
          $"Container failed to start on attempt {attempt}.{logs}",
          ex);
      }
    }

    throw new InvalidOperationException(
      $"Container failed to start on all {maxAttempts} attempts.", lastException);
  }

  private static bool IsSegfault(Exception ex) =>
    ex is ContainerNotRunningException && ex.Message.Contains("139");

  private static bool IsDotnetFirstTimeUseMutexFailure(Exception ex, string logs)
  {
    var text = ex + logs;
    return text.Contains("NuGet.Common.Migrations.MigrationRunner", StringComparison.Ordinal) &&
           text.Contains("Object synchronization method was called from an unsynchronized block of code", StringComparison.Ordinal);
  }

  private async Task StartContainerCoreAsync(CancellationToken ct)
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
      .WithEnvironment("EASYDOTNET_CONTAINER_TESTS", "1")
      .WithCommand("sh", "-c", installAndRun)
      .WithOutputConsumer(Consume.RedirectStdoutAndStderrToConsole())
      .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilMessageIsLogged(
          "Named pipe server started:",
          o => o
            .WithTimeout(StartupTimeout)
            .WithInterval(TimeSpan.FromSeconds(1))));

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
    await _pipe.ConnectAsync(10_000, ct);

    Rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_pipe, _pipe, CreateFormatter()));
    Rpc.TraceSource.Switch.Level = SourceLevels.All;
    Rpc.TraceSource.Listeners.Clear();
    Rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());
    OnConfigureRpc(Rpc);
    Rpc.StartListening();
  }

  private async Task TryDisposeContainerAsync()
  {
    try
    {
      if (_container is not null)
        await _container.DisposeAsync();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Container cleanup failed: {ex}");
    }
  }

  private async Task<string> TryGetLogsAsync(CancellationToken ct)
  {
    if (_container is null)
      return string.Empty;

    try
    {
      var (stdout, stderr) = await _container.GetLogsAsync(timestampsEnabled: false, ct: ct);
      return $"\nStdout:\n{stdout}\nStderr:\n{stderr}";
    }
    catch (Exception logEx)
    {
      return $"\nUnable to read container logs: {logEx.Message}";
    }
  }

  /// <summary>
  /// Called at the start of <see cref="StartAsync"/> before the container is built.
  /// Override to perform async setup (e.g. building a custom Docker image).
  /// </summary>
  protected virtual Task OnBeforeStartAsync(CancellationToken ct) => Task.CompletedTask;

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
    try
    {
      Rpc?.Dispose();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"RPC dispose failed: {ex}");
    }

    try
    {
      _pipe?.Dispose();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Pipe dispose failed: {ex}");
    }

    await TryDisposeContainerAsync();

    try
    {
      if (Directory.Exists(_toolInstallPath))
        Directory.Delete(_toolInstallPath, recursive: true);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Tool install cleanup failed for '{_toolInstallPath}': {ex}");
    }
  }
}