using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Extensions;
using EasyDotnet.MTP;
using EasyDotnet.MTP.RPC;
using EasyDotnet.Types;

namespace EasyDotnet.Services;

public class MtpService(OutFileWriterService outFileWriterService)
{
  public async Task<List<Types.DiscoveredTest>> RunDiscoverAsync(string testExecutablePath, CancellationToken token)
  {
    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }

    await using var client = await Client.CreateAsync(testExecutablePath);
    var discovered = await client.DiscoverTestsAsync(token);
    var tests = discovered.Where(x => x != null && x.Node != null).Select(x => x.ToDiscoveredTest()).ToList();
    return tests;
  }

  public async Task<List<TestRunResult>> RunTestsAsync(string testExecutablePath, RunRequestNode[] filter, CancellationToken token)
  {

    if (!File.Exists(testExecutablePath))
    {
      throw new FileNotFoundException("Test executable not found.", testExecutablePath);
    }
    await using var client = await Client.CreateAsync(testExecutablePath);
    var runResults = await client.RunTestsAsync(filter, token);
    var results = runResults.Select(x => x.ToTestRunResult()).ToList();
    return results;
  }
}