using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.MTP;
using EasyDotnet.Services;
using EasyDotnet.Types;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Test;

public class TestController(ClientService clientService, MtpService mtpService, VsTestService vsTestService, MsBuildService msBuildService) : BaseController
{

  [JsonRpcMethod("test/discover")]
  public async Task<IAsyncEnumerable<DiscoveredTest>> Discover(
    string projectPath,
    string? targetFrameworkMoniker,
    string? configuration,
    CancellationToken token)
  {

    if (!clientService.IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
    var project = await GetProject(projectPath, targetFrameworkMoniker, configuration, token);
    if (project.TestingPlatformDotnetTestSupport)
    {
      var path = GetExecutablePath(project);
      var res = await mtpService.RunDiscoverAsync(path, token);
      return res.AsAsyncEnumerable();
    }
    else
    {
      return vsTestService.RunDiscover(project.TargetPath!).AsAsyncEnumerable();
    }

  }

  [JsonRpcMethod("test/run")]
  public async Task<IAsyncEnumerable<TestRunResult>> Run(
    string projectPath,
    string targetFrameworkMoniker,
    string configuration,
    RunRequestNode[] filter,
    CancellationToken token
  )
  {
    if (!clientService.IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
    var project = await GetProject(projectPath, targetFrameworkMoniker, configuration, token);
    if (project.TestingPlatformDotnetTestSupport)
    {
      var path = GetExecutablePath(project);

      var res = await WithTimeout(
        (token) => mtpService.RunTestsAsync(path, filter, token),
        TimeSpan.FromMinutes(3),
        token
      );
      return res.AsAsyncEnumerable();
    }
    else
    {
      return vsTestService.RunTests(project.TargetPath!, [.. filter.Select(x => Guid.Parse(x.Uid))]).AsAsyncEnumerable();
    }


  }

  private static string GetExecutablePath(DotnetProjectProperties project) => OperatingSystem.IsWindows() ? Path.ChangeExtension(project.TargetPath!, ".exe") : Path.GetFileNameWithoutExtension(project.TargetPath!);

  private async Task<DotnetProjectProperties> GetProject(string projectPath, string? targetFrameworkMoniker, string? configuration, CancellationToken cancellationToken)
  {
    var project = await msBuildService.GetProjectPropertiesAsync(projectPath, targetFrameworkMoniker, configuration ?? "Debug", cancellationToken);
    return string.IsNullOrEmpty(project.TargetPath) ? throw new Exception("TargetPath is null or empty") : project;
  }

  private static async Task<T> WithTimeout<T>(
    Func<CancellationToken, Task<T>> func,
    TimeSpan timeout,
    CancellationToken callerToken
  )
  {
    using var timeoutCts = new CancellationTokenSource(timeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      callerToken,
      timeoutCts.Token
    );

    return await func(linkedCts.Token);
  }


}