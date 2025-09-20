using EasyDotnet.Domain.Models.Test;

namespace EasyDotnet.Infrastructure.Services;

public class MtpService
{
  public async Task<List<DiscoveredTest>> RunDiscoverAsync(string testExecutablePath, CancellationToken token)
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