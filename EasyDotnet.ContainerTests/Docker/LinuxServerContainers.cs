using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;

namespace EasyDotnet.ContainerTests.Docker;

public abstract class LinuxServerContainer(string image) : ServerContainer
{
  private readonly string _homePath = $"/tmp/easydotnet-home-{Guid.NewGuid():N}";

  protected override string Image => image;
  protected override string TmpMountPath => "/tmp";

  protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) =>
    builder
      .WithCreateParameterModifier(p => p.User = GetHostUserSpec())
      .WithEnvironment("HOME", _homePath)
      .WithEnvironment("DOTNET_CLI_HOME", _homePath)
      .WithEnvironment("XDG_DATA_HOME", $"{_homePath}/.local/share")
      .WithEnvironment("XDG_CONFIG_HOME", $"{_homePath}/.config")
      .WithEnvironment("NUGET_PACKAGES", $"{_homePath}/.nuget/packages")
      .WithEnvironment("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
      .WithEnvironment("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
      .WithEnvironment("DOTNET_NOLOGO", "1");

  /// <summary>
  /// The container writes its NuGet cache + tool install into <see cref="_homePath"/>, which
  /// lives under the bind-mounted <c>/tmp</c> and so survives on the host after <c>docker rm</c>.
  /// Delete it on teardown so these (~tens of MB each) don't accumulate across runs and fill /tmp.
  /// </summary>
  protected override ValueTask OnAfterDisposeAsync()
  {
    try
    {
      if (Directory.Exists(_homePath))
        Directory.Delete(_homePath, recursive: true);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Home dir cleanup failed for '{_homePath}': {ex}");
    }

    return ValueTask.CompletedTask;
  }

  /// <summary>
  /// Runs the container as the same UID:GID as the test process so the Unix Domain
  /// Socket at /tmp/CoreFxPipe_* is writable by the host process connecting from outside.
  /// </summary>
  private static string GetHostUserSpec()
  {
    var lines = File.ReadAllLines("/proc/self/status");
    var uid = lines.First(l => l.StartsWith("Uid:")).Split('\t')[1];
    var gid = lines.First(l => l.StartsWith("Gid:")).Split('\t')[1];
    return $"{uid}:{gid}";
  }
}

public sealed class Sdk8LinuxContainer() : LinuxServerContainer("mcr.microsoft.com/dotnet/sdk:8.0")
{
  public override int SdkMajorVersion => 8;
}

public sealed class Sdk9LinuxContainer() : LinuxServerContainer("mcr.microsoft.com/dotnet/sdk:9.0")
{
  public override int SdkMajorVersion => 9;
}

public sealed class Sdk10LinuxContainer() : LinuxServerContainer("mcr.microsoft.com/dotnet/sdk:10.0.201")
{
  public override int SdkMajorVersion => 10;
}

public abstract class DockerfileLinuxServerContainer(string dockerfile) : LinuxServerContainer("placeholder")
{
  private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> _builtImages = new();
  private string? _builtImageName;

  protected override string Image => _builtImageName ?? throw new InvalidOperationException($"Image for {dockerfile} has not been built yet; OnBeforeStartAsync must run first.");

  protected override async Task OnBeforeStartAsync(CancellationToken ct) =>
    _builtImageName = await _builtImages.GetOrAdd(dockerfile, f => new Lazy<Task<string>>(() => BuildImageAsync(f))).Value;

  private static async Task<string> BuildImageAsync(string dockerfile)
  {
    var dockerfileDir = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory, "..", "..", "..", "..",
      "EasyDotnet.ContainerTests", "Docker"));

    var image = new ImageFromDockerfileBuilder()
      .WithDockerfileDirectory(dockerfileDir)
      .WithDockerfile(dockerfile)
      .Build();

    await image.CreateAsync();
    return image.FullName;
  }
}

/// <summary>
/// A container with both .NET 8 and .NET 10 SDKs and runtimes installed.
/// .NET 10 is the default (highest) SDK; .NET 8 is also present.
/// DOTNET_ROLL_FORWARD=LatestMajor is set so that spawning BuildServer without
/// --fx-version picks .NET 10, reproducing the real-world failure where the IDE's
/// BuildHostFactory ignores the workspace global.json and the process lands on .NET 10.
/// </summary>
public sealed class MultiSdkLinuxContainer() : DockerfileLinuxServerContainer("Dockerfile.multisdk")
{
  public override int SdkMajorVersion => 10;

  protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) =>
    base.ConfigureContainer(builder)
      .WithEnvironment("DOTNET_ROLL_FORWARD", "LatestMajor");
}

/// <summary>
/// A container with the stable .NET 10 SDK and the .NET 11 RC SDK installed side by side.
/// Unlike <see cref="MultiSdkLinuxContainer"/> no roll-forward env vars are set, so it mirrors a
/// developer machine that has just installed a preview SDK.
/// </summary>
public sealed class RcSdkLinuxContainer() : DockerfileLinuxServerContainer("Dockerfile.rcsdk")
{
  public override int SdkMajorVersion => 11;
}
/// <summary>
/// A container with the .NET 10 SDK and the out of support .NET 6 SDK installed side by side.
/// No roll-forward env vars are set, so it mirrors a developer machine working in a legacy repo
/// whose global.json pins .NET 6.
/// </summary>
public sealed class Net6SdkLinuxContainer() : DockerfileLinuxServerContainer("Dockerfile.net6sdk")
{
  public override int SdkMajorVersion => 10;
}