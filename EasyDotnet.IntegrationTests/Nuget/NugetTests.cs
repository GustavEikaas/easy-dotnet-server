using EasyDotnet.IntegrationTests.Utils;

namespace EasyDotnet.IntegrationTests.Nuget;

public sealed record TestNugetSourceResponse(string Name, string Uri, bool IsLocal);

public class NugetTests
{

  [Fact]
  public async Task ListSources()
  {
    var enumerable = RpcTestServerInstantiator.InitializedOneShotStreamRequest<TestNugetSourceResponse>("nuget/list-sources", null);

    var list = new List<TestNugetSourceResponse>();

    await foreach (var item in enumerable)
    {
      list.Add(item);
    }

    Assert.NotEmpty(list);
  }
}