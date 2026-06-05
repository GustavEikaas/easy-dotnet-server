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
  private readonly string _fakeBinPath = Path.Combine(Path.GetTempPath(), $"easydotnet-fake-dotnet-{Guid.NewGuid():N}");

  public override int SdkMajorVersion => 10;

  protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) =>
    base.ConfigureContainer(builder)
      .WithEnvironment("PATH", $"{_fakeBinPath}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin");

  protected override Task OnBeforeStartAsync(CancellationToken ct)
  {
    Directory.CreateDirectory(_fakeBinPath);
    var fakeDotnetEfPath = Path.Combine(_fakeBinPath, "dotnet-ef");

    File.WriteAllText(fakeDotnetEfPath, """
      #!/bin/sh

      project_path=
      prev=
      for arg in "$@"; do
        if [ "$prev" = "--project" ]; then
          project_path=$arg
          break
        fi
        prev=$arg
      done

      project_dir=
      if [ -n "$project_path" ]; then
        project_dir=$(dirname "$project_path")
      fi

      if [ "$1" = "dbcontext" ] && [ "$2" = "list" ]; then
        cat <<'EOF'
      info: Build started...
      info: Build succeeded.
      data: [
      data:   {
      data:     "fullName": "App.AppDbContext",
      data:     "safeName": "AppDbContext",
      data:     "name": "AppDbContext",
      data:     "assemblyQualifiedName": "App.AppDbContext, App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
      data:   }
      data: ]
      EOF
        exit 0
      fi

      if [ "$1" = "migrations" ] && [ "$2" = "list" ]; then
        if [ -n "$project_dir" ] && [ -f "$project_dir/.empty-migrations" ]; then
          cat <<'EOF'
      info: Build started...
      info: Build succeeded.
      data: []
      EOF
          exit 0
        fi

        cat <<'EOF'
      info: Build started...
      info: Build succeeded.
      data: [
      data:   {
      data:     "id": "20260519070000_Initial",
      data:     "name": "Initial",
      data:     "safeName": "Initial",
      data:     "applied": true
      data:   },
      data:   {
      data:     "id": "20260520083538_Add_Maintenance_Notification",
      data:     "name": "Add_Maintenance_Notification",
      data:     "safeName": "Add_Maintenance_Notification",
      data:     "applied": null
      data:   }
      data: ]
      EOF
        exit 0
      fi

      echo "Unexpected dotnet-ef arguments: $@" >&2
      exit 1
      """);

    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(fakeDotnetEfPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    return Task.CompletedTask;
  }
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