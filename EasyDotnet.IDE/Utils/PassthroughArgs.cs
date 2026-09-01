using System.CommandLine.Parsing;

namespace EasyDotnet.IDE.Utils;

public static class PassthroughArgs
{
  private static readonly string[] ConfigurationFlags = ["-c", "--configuration", "/c", "/configuration"];

  public static List<string> Split(string? args) =>
      string.IsNullOrWhiteSpace(args) ? [] : [.. CommandLineParser.SplitCommandLine(args)];

  public static bool SpecifiesConfiguration(IEnumerable<string> args) =>
      args.Any(arg =>
          ConfigurationFlags.Contains(arg, StringComparer.OrdinalIgnoreCase)
          || arg.StartsWith("-p:Configuration=", StringComparison.OrdinalIgnoreCase)
          || arg.StartsWith("/p:Configuration=", StringComparison.OrdinalIgnoreCase)
          || arg.StartsWith("--property:Configuration=", StringComparison.OrdinalIgnoreCase));

  public static bool SpecifiesPlatform(IEnumerable<string> args) =>
      args.Any(arg =>
          arg.StartsWith("-p:Platform=", StringComparison.OrdinalIgnoreCase)
          || arg.StartsWith("/p:Platform=", StringComparison.OrdinalIgnoreCase)
          || arg.StartsWith("--property:Platform=", StringComparison.OrdinalIgnoreCase));
}