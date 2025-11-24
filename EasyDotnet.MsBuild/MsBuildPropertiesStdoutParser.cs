using System.Text.Json;

namespace EasyDotnet.MsBuild;

public static class MsBuildPropertiesStdoutParser
{
  public static DotnetProject ParseMsBuildOutputToProject(string stdout)
  {
    if (string.IsNullOrWhiteSpace(stdout))
    {
      throw new ArgumentException("MSBuild output is empty.", nameof(stdout));
    }

    var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    var jsonStartIndex = Array.FindIndex(lines, line => line.Trim() == "{");
    if (jsonStartIndex == -1)
    {
      throw new InvalidOperationException("Did not find JSON payload in MSBuild output.");
    }

    var jsonPayload = string.Join("\n", lines.Skip(jsonStartIndex));

    var msbuildOutput = JsonSerializer.Deserialize<MsBuildPropertiesResponse>(jsonPayload);
    if (msbuildOutput?.Properties == null)
    {
      throw new InvalidOperationException("Failed to deserialize MSBuild properties.");
    }

    var values = msbuildOutput.Properties;
    var bag = new MsBuildPropertyBag(values);

    return DotnetProjectDeserializer.FromBag(bag);
  }

  private class MsBuildPropertiesResponse
  {
    public Dictionary<string, string?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  }
}