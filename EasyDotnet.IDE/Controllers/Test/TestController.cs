using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.MTP;
using EasyDotnet.Services;
using EasyDotnet.Types;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Test;

public class TestController(IClientService clientService, MtpService mtpService, VsTestService vsTestService, IMsBuildService msBuildService) : BaseController
{

  [JsonRpcMethod("test/discover")]
  public async Task<IAsyncEnumerable<DiscoveredTest>> Discover(
    string projectPath,
    string? targetFrameworkMoniker = null,
    string? configuration = null,
    CancellationToken token = default)
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
      return vsTestService.RunDiscover(project.TargetPath!).AsAsyncEnumerable().WithJsonRpcSettings(new JsonRpcEnumerableSettings() { MinBatchSize = 30 });
    }
  }

  [JsonRpcMethod("test/run")]
  public async Task<IAsyncEnumerable<TestRunResult>> Run(
    string projectPath,
    string configuration,
    RunRequestNode[] filter,
    string? targetFrameworkMoniker = null,
    CancellationToken token = default
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
      return vsTestService.RunTests(project.TargetPath!, [.. filter.Select(x => Guid.Parse(x.Uid))]).AsAsyncEnumerable().WithJsonRpcSettings(new JsonRpcEnumerableSettings() { MinBatchSize = 30 });
    }
  }

  private static string GetExecutablePath(DotnetProjectV1 project) => OperatingSystem.IsWindows() ? Path.ChangeExtension(project.TargetPath!, ".exe") : project.TargetPath![..^4];

  private async Task<DotnetProjectV1> GetProject(string projectPath, string? targetFrameworkMoniker, string? configuration, CancellationToken cancellationToken)
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