namespace EasyDotnet.Infrastructure.EntityFramework;

public static class EfToolOutputParser
{
  public static EfCommandResult Parse(string output)
  {
    var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    var dataLines = new List<string>();
    var infoLines = new List<string>();
    var errorLines = new List<string>();

    foreach (var line in lines)
    {
      if (line.StartsWith("data:"))
      {
        dataLines.Add(line[5..].TrimStart());
      }
      else if (line.StartsWith("info:"))
      {
        infoLines.Add(line[5..].TrimStart());
      }
      else if (line.StartsWith("error:"))
      {
        errorLines.Add(line[6..].TrimStart());
      }
    }

    var jsonData = dataLines.Count > 0 ? ExtractJson(dataLines) : null;
    var errorMessage = errorLines.Count > 0 ? string.Join(" ", errorLines) : null;

    return new EfCommandResult(
      ExitCode: default,
      Success: errorLines.Count == 0,
      JsonData: jsonData,
      ErrorMessage: errorMessage,
      InfoMessages: [.. infoLines],
      ErrorMessages: [.. errorLines],
      StandardOutput: string.Empty,
      StandardError: string.Empty
    );
  }

  public static string ExtractJson(List<string> dataLines) =>
    string.Join(Environment.NewLine, dataLines);
}