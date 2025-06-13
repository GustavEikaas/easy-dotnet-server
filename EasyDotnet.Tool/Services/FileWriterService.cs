using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Types;

namespace EasyDotnet.Services;

public class OutFileWriterService
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  public void WriteTestRunResults(List<TestRunResult> results, string outFile)
  {

    using var writer = new StreamWriter(outFile, false);

    if (results.Count == 0)
    {
      writer.WriteLine("[]");
    }
    else
    {
      results.ToList().ForEach(x =>
          writer.WriteLine(JsonSerializer.Serialize(x, SerializerOptions).Replace("\n", "").Replace("\r", ""))
        );
    }
  }

  public void WriteDiscoveredTests(List<DiscoveredTest> testList, string outFile)
  {
    using var writer = new StreamWriter(outFile, false);

    if (testList.Count == 0)
    {
      writer.WriteLine("[]");
    }
    else
    {
      testList.ToList().ForEach(x =>
          writer.WriteLine(JsonSerializer.Serialize(x, SerializerOptions).Replace("\n", "").Replace("\r", ""))
        );
    }
  }

  public void WriteBuildResult(List<BuildMessage> messages, string outFile)
  {
    using var writer = new StreamWriter(outFile, false);

    if (messages.Count == 0)
    {
      writer.WriteLine("[]");
    }
    else
    {
      messages.ToList().ForEach(x =>
          writer.WriteLine(JsonSerializer.Serialize(x, SerializerOptions).Replace("\n", "").Replace("\r", ""))
        );
    }
  }

}
