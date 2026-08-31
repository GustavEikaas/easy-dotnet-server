using EasyDotnet.IntegrationTests.Initialize;
using EasyDotnet.IntegrationTests.Utils;

namespace EasyDotnet.IntegrationTests.TestRunner;

public sealed class QuickDiscoverPreloadTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "edpreload-" + Guid.NewGuid().ToString("N"));

  public QuickDiscoverPreloadTests()
  {
    Directory.CreateDirectory(_root);
    File.WriteAllText(Path.Combine(_root, "Example.slnx"), """
<Solution>
    <Project Path="Example.csproj" />
</Solution>
""");
    File.WriteAllText(Path.Combine(_root, "Example.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
""");
    File.WriteAllText(Path.Combine(_root, "GreetingTests.cs"), """
using Xunit;
namespace Example;
public class GreetingTests
{
  [Fact]
  public void Message_returns_hello() => Assert.Equal("hello", "hello");
}
""");
  }

  [Fact]
  public async Task QuickDiscover_Does_Not_Restore_Or_Build()
  {
    Environment.SetEnvironmentVariable(
        "EASYDOTNET_PROPERTY_CACHE_DIR",
        Path.Combine(Path.GetTempPath(), "edpc-" + Guid.NewGuid().ToString("N")));

    using var server = RpcTestServerInstantiator.GetUninitializedStreamServer();
    await server.InvokeWithParameterObjectAsync<TestInitializeResponse>(
        "initialize",
        new List<TestInitializeRequest> { new(new TestClientInfo("test", "3.0.0"), new TestProjectInfo(_root)) });

    await server.InvokeWithParameterObjectAsync<object>(
        "testrunner/quickDiscover",
        new { solutionPath = Path.Combine(_root, "Example.slnx") });

    Assert.False(Directory.Exists(Path.Combine(_root, "obj")), "quickDiscover must not restore");
    Assert.False(Directory.Exists(Path.Combine(_root, "bin")), "quickDiscover must not build");
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); } catch { }
  }
}