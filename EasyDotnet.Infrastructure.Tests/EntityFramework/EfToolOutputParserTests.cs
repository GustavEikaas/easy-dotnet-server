using EasyDotnet.Infrastructure.EntityFramework;

namespace EasyDotnet.Infrastructure.Tests.EntityFramework;

public class EfToolOutputParserTests
{
  [Test]
  public async Task Parse_WithSuccessfulJsonOutput_ReturnsCorrectParsedData()
  {
    const string output = """
            info:    Build started...
            info:    Build succeeded.
            data:    [
            data:      {
            data:         "fullName": "EF.Test.Contexts.AppDbContext",
            data:         "safeName": "AppDbContext",
            data:         "name": "AppDbContext",
            data:         "assemblyQualifiedName": "EF.Test.Contexts.AppDbContext, EF.Test, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
            data:      },
            data:      {
            data:         "fullName": "EF.Test.Contexts.LoggingDbContext",
            data:         "safeName": "LoggingDbContext",
            data:         "name": "LoggingDbContext",
            data:         "assemblyQualifiedName": "EF.Test.Contexts.LoggingDbContext, EF.Test, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
            data:      }
            data:    ]
            """;

    var result = EfToolOutputParser.Parse(output);

    await Assert.That(result.JsonData).IsNotNull();
    await Assert.That(result.JsonData).Contains("AppDbContext");
    await Assert.That(result.JsonData).Contains("LoggingDbContext");
    await Assert.That(result.ErrorMessage).IsNull();
    await Assert.That(result.InfoMessages).Count().IsEqualTo(2);
    await Assert.That(result.InfoMessages[0]).IsEqualTo("Build started...");
    await Assert.That(result.InfoMessages[1]).IsEqualTo("Build succeeded.");
    await Assert.That(result.ErrorMessages).IsEmpty();
  }

  [Test]
  public async Task Parse_WithBuildError_ReturnsErrorMessage()
  {
    const string output = """
            info:    Build started...
            error:   Build failed. Use dotnet build to see the errors.
            """;

    var result = EfToolOutputParser.Parse(output);

    await Assert.That(result.JsonData).IsNull();
    await Assert.That(result.ErrorMessage).IsEqualTo("Build failed. Use dotnet build to see the errors.");
    await Assert.That(result.InfoMessages).Count().IsEqualTo(1);
    await Assert.That(result.InfoMessages[0]).IsEqualTo("Build started...");
    await Assert.That(result.ErrorMessages).Count().IsEqualTo(1);
    await Assert.That(result.ErrorMessages[0]).IsEqualTo("Build failed. Use dotnet build to see the errors.");
  }

  [Test]
  public async Task Parse_WithEmptyOutput_ReturnsEmptyResult()
  {
    var output = string.Empty;

    var result = EfToolOutputParser.Parse(output);

    await Assert.That(result.JsonData).IsNull();
    await Assert.That(result.ErrorMessage).IsNull();
    await Assert.That(result.InfoMessages).IsEmpty();
    await Assert.That(result.ErrorMessages).IsEmpty();
  }
}