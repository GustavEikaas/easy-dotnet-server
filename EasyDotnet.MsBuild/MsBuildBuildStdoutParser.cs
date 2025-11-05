using System.Text.RegularExpressions;

namespace EasyDotnet.MsBuild;

public static partial class MsBuildBuildStdoutParser
{
  [GeneratedRegex(@"^(?<file>.*)\((?<line>\d+),(?<col>\d+)\): (?<type>error|warning) (?<code>\S+): (?<msg>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
  private static partial Regex MsBuildLoggingLine();

  public static IEnumerable<MsBuildStdoutMessage> ParseMsBuildLines(string output)
  {
    var regex = MsBuildLoggingLine();
    return output
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => regex.Match(line))
        .Where(match => match.Success)
        .Select(match => new MsBuildStdoutMessage(
            Type: match.Groups["type"].Value,
            FilePath: match.Groups["file"].Value.Trim(),
            LineNumber: int.Parse(match.Groups["line"].Value),
            ColumnNumber: int.Parse(match.Groups["col"].Value),
            Code: match.Groups["code"].Value,
            Message: match.Groups["msg"].Value
        ));
  }

  public static (List<MsBuildStdoutMessage> Errors, List<MsBuildStdoutMessage> Warnings) ParseBuildOutput(string stdout, string stderr)
  {
    var messages = ParseMsBuildLines(stdout)
        .Concat(
            stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                  .Select(line => new MsBuildStdoutMessage("error", "", 0, 0, "", line))
        );

    var errors = messages
        .Where(m => m.Type.Equals("error", StringComparison.OrdinalIgnoreCase))
        .GroupBy(m => (m.Type, m.Code, m.LineNumber, m.ColumnNumber))
        .Select(g => g.First())
        .ToList();

    var warnings = messages
        .Where(m => m.Type.Equals("warning", StringComparison.OrdinalIgnoreCase))
        .GroupBy(m => (m.Type, m.Code, m.LineNumber, m.ColumnNumber))
        .Select(g => g.First())
        .ToList();

    return (errors, warnings);
  }
}