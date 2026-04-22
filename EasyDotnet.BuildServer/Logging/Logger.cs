using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyDotnet.BuildServer.Logging;

public sealed class Logger(LogLevelState state)
{
  private static readonly Regex TemplateRegex = new(@"\{([^{}:]+)(?::[^{}]*)?\}", RegexOptions.Compiled);

  public void LogDebug(string template, params object?[] args) => Write(SourceLevels.Verbose, null, template, args);
  public void LogInformation(string template, params object?[] args) => Write(SourceLevels.Information, null, template, args);
  public void LogWarning(string template, params object?[] args) => Write(SourceLevels.Warning, null, template, args);
  public void LogError(string template, params object?[] args) => Write(SourceLevels.Error, null, template, args);
  public void LogError(Exception? ex, string template, params object?[] args) => Write(SourceLevels.Error, ex, template, args);
  public void LogDebug(Exception? ex, string template, params object?[] args) => Write(SourceLevels.Verbose, ex, template, args);

  public void Log(SourceLevels level, string template, params object?[] args) => Write(level, null, template, args);

  private void Write(SourceLevels level, Exception? ex, string template, object?[] args)
  {
    if (!IsEnabled(level)) return;

    var formatted = FormatTemplate(template, args);
    var tag = LevelTag(level);
    var sb = new StringBuilder();
    sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(tag).Append("] [BuildServer] ").Append(formatted);

    if (ex is not null)
    {
      sb.Append(Environment.NewLine).Append(ex);
    }

    var line = sb.ToString();
    Console.Error.WriteLine(line);
    state.RingSink.Add(line);
  }

  public bool IsEnabled(SourceLevels level) =>
      level != SourceLevels.Off && ((int)state.Current & (int)level) == (int)level;

  private static string LevelTag(SourceLevels level) => level switch
  {
    SourceLevels.Verbose => "DBG",
    SourceLevels.Information => "INF",
    SourceLevels.Warning => "WRN",
    SourceLevels.Error => "ERR",
    SourceLevels.Critical => "FTL",
    _ => "INF",
  };

  private static string FormatTemplate(string template, object?[] args)
  {
    if (args is null || args.Length == 0) return template;

    var index = 0;
    return TemplateRegex.Replace(template, _ =>
    {
      if (index >= args.Length) return "?";
      var value = args[index++];
      return value?.ToString() ?? "";
    });
  }
}
