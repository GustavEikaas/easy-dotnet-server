using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.Debugger.ValueConverters;
using Microsoft.Extensions.Logging;
using Moq;

namespace EasyDotnet.Debugger.Tests.ValueConverters;

public class ListValueConverterTests
{
  private static VariablesResponse CreateBaseResponse(List<Variable> variables) => new()
  {
    Command = "Variables",
    RequestSeq = 1,
    Seq = 1,
    Success = true,
    Type = "response",
    Body = new VariablesResponseBody { Variables = variables }
  };

  private static ListValueConverter CreateConverter()
  {
    var logger = new Mock<ILogger<IValueConverter>>();
    return new ListValueConverter(logger.Object);
  }

  [Test]
  public async Task CanConvert_ReturnsTrue_WhenListInternalFieldsPresent()
  {
    var response = CreateBaseResponse(
        [
            new Variable { Name = "_items", Type = "", Value="Object", VariablesReference = 10 },
                new Variable { Name = "_size", Type = "int", Value = "3" },
                new Variable { Name = "Capacity", Type = "int", Value = "10" }
        ]);

    var converter = CreateConverter();

    await Assert.That(converter.CanConvert(response)).IsTrue();
  }

  [Test]
  public async Task CanConvert_ReturnsFalse_WhenFieldsMissing()
  {
    var response = CreateBaseResponse(
        [
            new Variable { Name = "_items", Type="Object", Value="Object", VariablesReference = 10 },
            new Variable { Name = "_size", Type="int", Value = "3" }
        ]);

    var converter = CreateConverter();

    await Assert.That(converter.CanConvert(response)).IsFalse();
  }


  [Test]
  public async Task TryConvertAsync_ReplacesVariables_With_Actual_Items()
  {
    var converter = CreateConverter();

    var response = CreateBaseResponse(
        [
            new Variable { Name = "_items", Type="int", Value="Object", VariablesReference = 5 },
            new Variable { Name = "_size", Value = "3", Type = "int" },
            new Variable { Name = "Capacity", Value = "10", Type ="int" }
        ]);

    var itemsArrayResponse = CreateBaseResponse(
        [
            new Variable { Name = "[0]", Value = "\"A\"", Type="string" },
                new Variable { Name = "[1]", Value = "\"B\"", Type="string" },
                new Variable { Name = "[2]", Value = "\"C\"", Type = "string"},
                new Variable { Name = "[3]", Value = "\"EXTRA1\"", Type = "string" },
                new Variable { Name = "[4]", Value = "\"EXTRA2\"", Type="string" }
        ]);

    var proxy = new Mock<IDebuggerProxy>();
    proxy
        .Setup(p => p.GetVariablesAsync(5, It.IsAny<CancellationToken>()))
        .ReturnsAsync(itemsArrayResponse);

    var result = await converter.TryConvertAsync(response, proxy.Object, CancellationToken.None);

    await Assert.That(result).IsTrue();
    await Assert.That(response.Body.Variables.Count).IsEqualTo(3);

    var values = response.Body.Variables.Select(v => v.Value).ToList();
    await Assert.That(values).IsEquivalentTo(["\"A\"", "\"B\"", "\"C\""]);
  }
}

//   [Test]
//   public async Task TryConvertAsync_ReturnsFalse_When_ItemsVariableIsMissingReference()
//   {
//     var converter = CreateConverter(out var logger);
//
//     var response = CreateBaseResponse(
//         [
//             new Variable { Name = "_items", VariablesReference = 0 },
//                 new Variable { Name = "_size", Value = "3" },
//                 new Variable { Name = "Capacity", Value = "10" }
//         ]);
//
//     var proxy = new Mock<IDebuggerProxy>();
//
//     var result = await converter.TryConvertAsync(response, proxy.Object, CancellationToken.None);
//
//     await Assert.That(result).IsFalse();
//   }
//
//   [Test]
//   public async Task TryConvertAsync_ReturnsFalse_When_SizeNotParsable()
//   {
//     var converter = CreateConverter(out var logger);
//
//     var response = CreateBaseResponse(
//         [
//             new Variable { Name = "_items", VariablesReference = 3 },
//                 new Variable { Name = "_size", Value = "NOT_INT" },
//                 new Variable { Name = "Capacity", Value = "10" }
//         ]);
//
//     var proxy = new Mock<IDebuggerProxy>();
//
//     var result = await converter.TryConvertAsync(response, proxy.Object, CancellationToken.None);
//
//     await Assert.That(result).IsFalse();
//
//     // verify log warning was produced
//     logger.VerifyLogWarning("Could not parse _size value: NOT_INT");
//   }
//
//   [Test]
//   public async Task TryConvertAsync_ReturnsFalse_When_ItemsArrayResponseHasNoVariables()
//   {
//     var converter = CreateConverter(out var logger);
//
//     var response = CreateBaseResponse(
//         [
//             new Variable { Name = "_items", VariablesReference = 10 },
//                 new Variable { Name = "_size", Value = "2" },
//                 new Variable { Name = "Capacity", Value = "10" }
//         ]);
//
//     var itemsArrayResponse = CreateBaseResponse(new List<Variable>()); // Empty list
//
//     var proxy = new Mock<IDebuggerProxy>();
//     proxy
//         .Setup(p => p.GetVariablesAsync(10, It.IsAny<CancellationToken>()))
//         .ReturnsAsync(itemsArrayResponse);
//
//     var result = await converter.TryConvertAsync(response, proxy.Object, CancellationToken.None);
//
//     await Assert.That(result).IsFalse();
//   }
//
//   [Test]
//   public async Task TryConvertAsync_OrdersItemsByArrayIndex()
//   {
//     var converter = CreateConverter(out _);
//
//     var response = CreateBaseResponse(
//         [
//             new Variable { Name = "_items", VariablesReference = 7 },
//                 new Variable { Name = "_size", Value = "3" },
//                 new Variable { Name = "Capacity", Value = "10" }
//         ]);
//
//     var itemsArrayResponse = CreateBaseResponse(
//         [
//             new Variable { Name = "[2]", Value = "\"C\"" },
//                 new Variable { Name = "[0]", Value = "\"A\"" },
//                 new Variable { Name = "[1]", Value = "\"B\"" }
//         ]);
//
//     var proxy = new Mock<IDebuggerProxy>();
//     proxy
//         .Setup(p => p.GetVariablesAsync(7, It.IsAny<CancellationToken>()))
//         .ReturnsAsync(itemsArrayResponse);
//
//     var result = await converter.TryConvertAsync(response, proxy.Object, CancellationToken.None);
//
//     await Assert.That(result).IsTrue();
//
//     var values = response.Body.Variables.Select(v => v.Value).ToList();
//     await Assert.That(values).IsEqualTo(new[] { "\"A\"", "\"B\"", "\"C\"" });
//   }
// }