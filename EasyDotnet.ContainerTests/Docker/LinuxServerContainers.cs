using DotNet.Testcontainers.Builders;

namespace EasyDotnet.ContainerTests.Docker;

public abstract class LinuxServerContainer(string image) : ServerContainer
{
  protected override string Image => image;
  protected override string TmpMountPath => "/tmp";

  protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) =>
    builder
      .WithCreateParameterModifier(p => p.User = GetHostUserSpec())
      .WithEnvironment("HOME", "/tmp")
      .WithEnvironment("XDG_DATA_HOME", "/tmp/.local/share")
      .WithEnvironment("XDG_CONFIG_HOME", "/tmp/.config")
      .WithEnvironment("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
      .WithEnvironment("DOTNET_NOLOGO", "1");

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

public sealed class Sdk10LinuxContainer() : LinuxServerContainer("mcr.microsoft.com/dotnet/sdk:10.0")
{
  public override int SdkMajorVersion => 10;
}

/// <summary>
/// A container with both .NET 8 and .NET 10 SDKs and runtimes installed.
/// .NET 10 is the default (highest) SDK; .NET 8 is also present.
/// DOTNET_ROLL_FORWARD=LatestMajor is set so that spawning BuildServer without
/// --fx-version picks .NET 10, reproducing the real-world failure where the IDE's
/// BuildHostFactory ignores the workspace global.json and the process lands on .NET 10.
/// </summary>
public sealed class MultiSdkLinuxContainer() : LinuxServerContainer("placeholder")
{
  private static readonly SemaphoreSlim _buildLock = new(1, 1);
  private static string? _builtImageName;
  public override int SdkMajorVersion => 10;
  protected override string Image => _builtImageName ?? throw new InvalidOperationException("MultiSdkLinuxContainer image has not been built yet; OnBeforeStartAsync must run first.");

  protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) =>
    base.ConfigureContainer(builder)
      .WithEnvironment("DOTNET_ROLL_FORWARD", "LatestMajor");

  protected override async Task OnBeforeStartAsync(CancellationToken ct)
  {
    if (_builtImageName is not null)
      return;

    await _buildLock.WaitAsync(ct);
    try
    {
      if (_builtImageName is not null)
        return;

      var dockerfileDir = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "EasyDotnet.ContainerTests", "Docker"));

      var image = new ImageFromDockerfileBuilder()
        .WithDockerfileDirectory(dockerfileDir)
        .WithDockerfile("Dockerfile.multisdk")
        .Build();

      await image.CreateAsync(ct);
      _builtImageName = image.FullName;
    }
    finally
    {
      _buildLock.Release();
    }
  }
}