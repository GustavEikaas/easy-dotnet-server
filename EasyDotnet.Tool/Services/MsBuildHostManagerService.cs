using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EasyDotnet.MsBuild.Contracts;
using EasyDotnet.Utils;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.Services;

public enum BuildClientType
{
  Sdk,
  Framework
}

public interface IMsBuildHostManager
{
  Task<MsBuildHost> GetOrStartClientAsync(BuildClientType type);
  void StopAll();
}

public class MsBuildHostManager(JsonRpc server) : IMsBuildHostManager, IDisposable
{
  private const int MaxPipeNameLength = 104;
  private readonly string _sdk_Pipe = GeneratePipeName(BuildClientType.Sdk);
  private readonly string _framework_Pipe = GeneratePipeName(BuildClientType.Framework);

  private readonly ConcurrentDictionary<string, MsBuildHost> _buildClientCache = new();


  public async Task<MsBuildHost> GetOrStartClientAsync(BuildClientType type)
  {
    var client = _buildClientCache.AddOrUpdate(
    type == BuildClientType.Sdk ? _sdk_Pipe : _framework_Pipe,
    key => new MsBuildHost(key),
    (key, existingClient) =>
      existingClient ?? new MsBuildHost(key));

    await client.ConnectAsync(ensureServerStarted: true, server.TraceSource.Switch.Level);
    return client;
  }


  private static string GeneratePipeName(BuildClientType type)
  {
    var pipePrefix = "CoreFxPipe_";
    var pipeName = "EasyDotnet_MSBuild_";
    var uid = Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
    var name = $"{pipeName}{type}_{uid}";
    var maxNameLength = MaxPipeNameLength - Path.GetTempPath().Length - pipePrefix.Length - 1;
    return name[..Math.Min(name.Length, maxNameLength)];
  }

  public void StopAll()
  {
    _buildClientCache.Values.ToList().ForEach(x => x.StopServer());
    _buildClientCache.Clear();
  }

  public void Dispose()
  {
    StopAll();
    GC.SuppressFinalize(this);
  }
}

public class MsBuildHost(string pipeName)
{
  private JsonRpc? _rpc;
  private Process? _serverProcess;
  private Task? _connectTask;
  private readonly object _connectLock = new();
  private readonly string _pipeName = pipeName;

  public Task ConnectAsync(bool ensureServerStarted = true, SourceLevels? sourceLevel = null)
  {
    lock (_connectLock)
    {
      _connectTask ??= ConnectInternalAsync(ensureServerStarted, sourceLevel ?? SourceLevels.Off);
      return _connectTask;
    }
  }

  public bool IsAlive() => _serverProcess is not null && !_serverProcess.HasExited;

  private async Task ConnectInternalAsync(bool ensureServerStarted, SourceLevels sourceLevel)
  {
    if (ensureServerStarted)
    {
      _serverProcess = BuildServerStarter.StartBuildServer(_pipeName, sourceLevel);
      await Task.Delay(1000);
      if (_serverProcess.HasExited)
      {
        throw new InvalidOperationException($"Build server process exited prematurely. Exit code: {_serverProcess.ExitCode}");
      }
    }

    var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await stream.ConnectAsync(5000);

    var jsonMessageFormatter = new JsonMessageFormatter
    {
      JsonSerializer = { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } }
    };

    var handler = new HeaderDelimitedMessageHandler(stream, stream, jsonMessageFormatter);
    _rpc = new JsonRpc(handler);
    _rpc.StartListening();
  }

  public JsonRpc EnsureRpcAlive()
  {
    if (_rpc == null)
    {
      throw new InvalidOperationException("BuildClient not connected.");
    }
    if (!IsAlive())
    {
      throw new InvalidOperationException("BuildServer has crashed");
    }
    return _rpc;
  }

  public async Task<BuildResult> BuildAsync(string targetPath, string configuration)
  {
    var rpc = EnsureRpcAlive();
    var request = new { TargetPath = targetPath, Configuration = configuration };
    return await rpc.InvokeWithParameterObjectAsync<BuildResult>("msbuild/build", request);
  }

  public async Task<SdkInstallation[]> QuerySdkInstallations()
  {
    var rpc = EnsureRpcAlive();
    return await rpc.InvokeAsync<SdkInstallation[]>("msbuild/sdk-installations");
  }

  public void StopServer()
  {
    if (_serverProcess != null && !_serverProcess.HasExited)
    {
      _serverProcess.Kill(true);
      _serverProcess.Dispose();
    }
  }
}

public static class BuildServerStarter
{
  public static Process StartBuildServer(string pipeName, SourceLevels sourceLevel)
  {
    var dir = HostDirectoryUtil.HostDirectory;

#if DEBUG
    var exePath = Path.Combine(
        dir,
        "EasyDotnet.MsBuildSdk", "bin", "Debug", "net8.0", "EasyDotnet.MsBuildSdk.dll");
#else
    var exeHost = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    var exePath = Path.Combine(exeHost, "MsBuildSdk", "EasyDotnet.MsBuildSdk.dll");
#endif

    if (!File.Exists(exePath))
    {
      throw new FileNotFoundException("Build server executable not found.", exePath);
    }

    var arguments = $"\"{exePath}\" {pipeName} --logLevel {sourceLevel}";

    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = arguments,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true
    };

    var process = new Process { StartInfo = startInfo };
    process.Start();

    Console.WriteLine($"Started BuildServer from: {exePath}");

    return process;
  }
}