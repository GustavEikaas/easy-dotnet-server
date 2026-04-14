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