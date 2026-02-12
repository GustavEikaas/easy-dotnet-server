## MTP
**Line numbers**
1 index based, points to the `[Test] attribute`

### MSTest
### xUnit
### NUnit
### TUnit
**Version** 0.57.65
**Test**
```cs
  [Test]
  public async Task Parse_ValidRequestJson_ReturnsRequest()
  {
    var json = @"{
                ""seq"": 1,
                ""type"": ""request"",
                ""command"": ""initialize"",
                ""arguments"": { ""someArg"": 123 }
            }";

    var result = DapMessageDeserializer.Parse(json);

    await Assert.That(result).IsTypeOf<Request>();
    var req = (Request)result;
    await Assert.That(req.Seq).IsEqualTo(1);
    await Assert.That(req.Command).IsEqualTo("initialize");
    await Assert.That(req.Arguments?.GetProperty("someArg").GetInt32()).IsEqualTo(123);
  }
```
**Output**
```
DisplayName: "Parse_ValidRequestJson_ReturnsRequest"
Duration: null
ExecutionState: "discovered"
FilePath: "/home/gus/repo/easy-dotnet-server-test/EasyDotnet.Debugger.Tests/Dap/DapMessageDeserializerTests.cs"
LineEnd: 9
LineStart: 9
Message: null
MethodArity: 0
NodeType: "action"
StackTrace: null
StandardOutput: null
TestMethod: "Parse_ValidRequestJson_ReturnsRequest"
TestNamespace: null
TestType: "EasyDotnet.Debugger.Tests.Dap.DapMessageDeserializerTests"
Uid: "EasyDotnet.Debugger.Tests.Dap.DapMessageDeserializerTests.1.1.Parse_ValidRequestJson_ReturnsRequest.1.1.0"
```
### Expecto
**Version** 10.2.3

**Test**
```fs
[<Tests>]
let tests =
  testList "samples" [
    testCase "universe exists (╭ರᴥ•́)" <| fun _ ->
      let subject = true
      Expect.isTrue subject "I compute, therefore I am."
```

**Output**
```
DisplayName: "samples.universe exists (╭ರᴥ•́)"
Duration: null
ExecutionState: "discovered"
FilePath: "/home/gus/repo/TestPlatform.Playground/MTP.Expecto.Tests/Sample.fs"
LineEnd: 9
LineStart: 9
Message: null
MethodArity: null
NodeType: "action"
StackTrace: null
StandardOutput: null
TestMethod: null
TestNamespace: null
TestType: null
Uid: "22631464-7bae-92d1-539e-4c81447fdcb3"
```

## VSTest

**Line numbers**
0 index based, points to the `public void xxx`

### MSTest
**Version** 3.6.4

**Test**
```cs
[TestClass]
public sealed class Test1
{
    [TestMethod]
    public void TestMethod1()
    {
    }
}
```
**Output**
``` 
  CodeFilePath: "/home/gus/repo/easy-dotnet-server-test/EasyDotnet.MSTesty/Test1.cs"
  ContainsManagedMethodAndType: true
  DisplayName: "TestMethod1"
  ExecutorUri: "executor://mstestadapter/v2"
  FullyQualifiedName: "EasyDotnet.MSTesty.Test1.TestMethod1"
  Id: "8ef7c0f0-be0c-5cf9-0e21-61a29fb63dfa"
  LineNumber: 8
  LocalExtensionData: null
  ManagedMethod: "TestMethod1"
  ManagedType: "EasyDotnet.MSTesty.Test1"
  Source: "/home/gus/repo/easy-dotnet-server-test/EasyDotnet.MSTesty/bin/Debug/net8.0/EasyDotnet.MSTesty.dll"
```
### xUnit
**Version** 2.9.2
**Test**
```cs
  [Fact]
  public void TestServiceProviderHasRequiredServicesForControllers()
  {
    var jsonRpc = RpcTestServerInstantiator.GetUninitializedStreamServer();
    var sp = DiModules.BuildServiceProvider(jsonRpc, System.Diagnostics.SourceLevels.Off);
    AssemblyScanner.GetControllerTypes().ForEach(x => sp.GetRequiredService(x));
  }
```

**Output**
```
CodeFilePath: "/home/gus/repo/easy-dotnet-server-test/EasyDotnet.IntegrationTests/ServiceProviderTests.cs"
ContainsManagedMethodAndType: false
DisplayName: "EasyDotnet.IntegrationTests.ServiceProviderTests.TestServiceProviderHasRequiredServicesForControllers"
ExecutorUri: "executor://xunit/VsTestRunner2/netcoreapp"
FullyQualifiedName: "EasyDotnet.IntegrationTests.ServiceProviderTests.TestServiceProviderHasRequiredServicesForControllers"
Id: "9d6ce075-d851-ddc5-139c-f2e20c48df17"
LineNumber: 11
LocalExtensionData: null
ManagedMethod: null
ManagedType: null
Source: "/home/gus/repo/easy-dotnet-server-test/EasyDotnet.IntegrationTests/bin/Debug/net8.0/EasyDotnet.IntegrationTests.dll"
```

### NUnit
### Expecto
