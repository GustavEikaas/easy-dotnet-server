using EasyDotnet.IDE.Utils;

namespace EasyDotnet.IDE.Tests.Utils;

public sealed class PassthroughArgsTests
{
  [Test]
  public async Task Split_ReturnsEmptyForNullOrBlank()
  {
    await Assert.That(PassthroughArgs.Split(null).Count).IsEqualTo(0);
    await Assert.That(PassthroughArgs.Split("").Count).IsEqualTo(0);
    await Assert.That(PassthroughArgs.Split("   ").Count).IsEqualTo(0);
  }

  [Test]
  public async Task Split_SplitsIntoSeparateArgvEntries()
  {
    var args = PassthroughArgs.Split("-c Release -v minimal");

    await Assert.That(args).IsEquivalentTo(["-c", "Release", "-v", "minimal"]);
  }

  [Test]
  public async Task Split_KeepsQuotedValuesTogether()
  {
    var args = PassthroughArgs.Split("-p:Foo=\"a b\" --name \"John Doe\"");

    await Assert.That(args).IsEquivalentTo(["-p:Foo=a b", "--name", "John Doe"]);
  }

  [Test]
  [Arguments("-c")]
  [Arguments("--configuration")]
  [Arguments("-p:Configuration=Release")]
  [Arguments("/p:configuration=Release")]
  [Arguments("--property:Configuration=Release")]
  public async Task SpecifiesConfiguration_DetectsUserSuppliedConfiguration(string arg)
  {
    await Assert.That(PassthroughArgs.SpecifiesConfiguration(["-v", "minimal", arg])).IsTrue();
  }

  [Test]
  public async Task SpecifiesConfiguration_IgnoresUnrelatedArguments()
  {
    await Assert.That(PassthroughArgs.SpecifiesConfiguration(["-v", "minimal", "-p:Platform=x64"])).IsFalse();
    await Assert.That(PassthroughArgs.SpecifiesConfiguration([])).IsFalse();
  }

  [Test]
  public async Task SpecifiesPlatform_DetectsUserSuppliedPlatform()
  {
    await Assert.That(PassthroughArgs.SpecifiesPlatform(["-p:Platform=x64"])).IsTrue();
    await Assert.That(PassthroughArgs.SpecifiesPlatform(["-p:Configuration=Release"])).IsFalse();
  }
}