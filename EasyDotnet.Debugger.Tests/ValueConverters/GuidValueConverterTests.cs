using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.ValueConverters;
using Moq;

namespace EasyDotnet.Debugger.Tests.ValueConverters;

public class GuidValueConverterTests
{
  private static VariablesResponse CreateBaseResponse(List<Variable> variables) => new()
  {
    Command = "Variables",
    RequestSeq = 1,
    Seq = 1,
    Success = true,
    Type = "response",
    Body = new VariablesResponseBody() { Variables = variables }
  };

  [Test]
  public async Task CanConvert_ReturnsTrue_WhenGuidWithVariablesReference()
  {
    var response = CreateBaseResponse([        new Variable
            {
                Name = "x",
                Value = "{System.Guid}",
                Type = "System.Guid",
                VariablesReference = 5
            }
]);

    var converter = new GuidValueConverter();
    await Assert.That(converter.CanConvert(response)).IsTrue();
  }

  [Test]
  public async Task CanConvert_ReturnsFalse_WhenNoGuid()
  {
    var response = CreateBaseResponse([
        new Variable
            {
                Name = "x",
                Value = "\"hello\"",
                Type = "string",
                VariablesReference = 4
            }
    ]);

    var converter = new GuidValueConverter();
    await Assert.That(converter.CanConvert(response)).IsFalse();
  }

  [Test]
  public async Task TryConvertAsync_ReconstructsGuidCorrectly()
  {
    const int a = 1543583988;
    const short b = 23662;
    const short c = 18852;
    const byte d = 133;
    const byte e = 136;
    const byte f = 114;
    const byte g = 68;
    const byte h = 173;
    const byte i = 2;
    const byte j = 88;
    const byte k = 5;

    var expected = new Guid(a, b, c, d, e, f, g, h, i, j, k);

    var parent = CreateBaseResponse([
        new Variable
            {
                Name = "myGuid",
                Type = "System.Guid",
                Value = "{System.Guid}",
                VariablesReference = 10
            }
    ]);

    var fields = CreateBaseResponse([
        new Variable { Name = "_a", Value = a.ToString(), Type = "int" },
            new Variable { Name = "_b", Value =b.ToString(), Type = "short" },
            new Variable { Name = "_c", Value =c.ToString(), Type = "short" },
            new Variable { Name = "_d", Value =d.ToString(), Type = "byte" },
            new Variable { Name = "_e", Value =e.ToString(), Type = "byte" },
            new Variable { Name = "_f", Value =f.ToString(), Type = "byte" },
            new Variable { Name = "_g", Value =g.ToString(), Type = "byte" },
            new Variable { Name = "_h", Value =h.ToString(), Type = "byte" },
            new Variable { Name = "_i", Value =i.ToString(), Type = "byte" },
            new Variable { Name = "_j", Value =j.ToString(), Type = "byte" },
            new Variable { Name = "_k", Value =k.ToString(), Type = "byte" }
    ]);

    var mockProxy = new Mock<IDebuggerProxy>();
    mockProxy
        .Setup(p => p.GetVariablesAsync(10, It.IsAny<CancellationToken>()))
        .ReturnsAsync(fields);

    var converter = new GuidValueConverter();

    var result = await converter.TryConvertAsync(parent, mockProxy.Object, CancellationToken.None);

    await Assert.That(result).IsTrue();
    await Assert.That(parent.Body!.Variables[0].VariablesReference).IsEqualTo(0);
    await Assert.That(parent.Body.Variables[0].Value).IsEqualTo(expected.ToString());
  }

  [Test]
  public async Task TryConvertAsync_ReturnsFalse_WhenNoVariablesReference()
  {
    var response = CreateBaseResponse([
        new Variable
            {
                Name = "x",
                Type = "System.Guid",
                Value = "{System.Guid}",
                VariablesReference = 0
            }
    ]);

    var mockProxy = new Mock<IDebuggerProxy>();
    mockProxy
        .Setup(p => p.GetVariablesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateBaseResponse([]));

    var converter = new GuidValueConverter();

    var result = await converter.TryConvertAsync(response, mockProxy.Object, CancellationToken.None);

    await Assert.That(result).IsFalse();
  }
}