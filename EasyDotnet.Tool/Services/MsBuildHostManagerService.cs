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

public class MsBuildHostManager : IMsBuildHostManager, IDisposable
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

    await client.ConnectAsync(ensureServerStarted: true);
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

  public void Dispose() => StopAll();
}

public class MsBuildHost(string pipeName)
{
  private JsonRpc? _rpc;
  private Process? _serverProcess;
  private Task? _connectTask;
  private readonly object _connectLock = new();
  private readonly string _pipeName = pipeName;

  public Task ConnectAsync(bool ensureServerStarted = true)
  {
    lock (_connectLock)
    {
      _connectTask ??= ConnectInternalAsync(ensureServerStarted);
      return _connectTask;
    }
  }

  private async Task ConnectInternalAsync(bool ensureServerStarted)
  {
    if (ensureServerStarted)
    {
      _serverProcess = BuildServerStarter.StartBuildServer(_pipeName);
      await Task.Delay(1000);
    }

    var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await stream.ConnectAsync();

    var jsonMessageFormatter = new JsonMessageFormatter
    {
      JsonSerializer = { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } }
    };

    var handler = new HeaderDelimitedMessageHandler(stream, stream, jsonMessageFormatter);
    _rpc = new JsonRpc(handler);
    _rpc.StartListening();
  }

  public async Task<BuildResult> BuildAsync(string targetPath, string configuration)
  {
    if (_rpc == null)
      throw new InvalidOperationException("BuildClient not connected.");

    var request = new { TargetPath = targetPath, Configuration = configuration };
    return await _rpc.InvokeWithParameterObjectAsync<BuildResult>("msbuild/build", request);
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
  public static Process StartBuildServer(string pipeName)
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

    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"\"{exePath}\" {pipeName}",
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