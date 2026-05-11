using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyDotnet.IDE.Services;

public sealed record ProfilerSqlEvent(
    string File,
    int Line,
    string Sql,
    string? Parameters,
    double ElapsedMs);

public sealed record ProfilerSqlBucket(
    string BucketId,
    string File,
    int Line,
    string SqlSample,
    string? ParametersSample,
    long Count,
    long TotalMs,
    long MaxMs);

/// <summary>
/// Coalesces individual EF Core CommandExecuted events into per-site buckets keyed by
/// (file, line, normalized-sql). Normalization strips literal values so the same query
/// shape with different parameter values aggregates to one bucket.
/// </summary>
public static class SqlAggregator
{
  private static readonly Regex NumericLiteral = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);
  private static readonly Regex StringLiteral = new(@"'(?:[^']|'')*'", RegexOptions.Compiled);
  private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

  /// <summary>
  /// Produces a stable normalization key for a SQL statement: collapses whitespace and
  /// replaces numeric/string literals with placeholders. Parameter markers (<c>@p0</c>) are
  /// preserved — same query shape with different parameter values yields the same key.
  /// </summary>
  public static string NormalizeForKey(string sql)
  {
    if (string.IsNullOrEmpty(sql)) return string.Empty;
    var s = StringLiteral.Replace(sql, "?");
    s = NumericLiteral.Replace(s, "?");
    s = Whitespace.Replace(s, " ").Trim();
    return s;
  }

  public static IReadOnlyList<ProfilerSqlBucket> Aggregate(IEnumerable<ProfilerSqlEvent> events)
  {
    var byKey = new Dictionary<(string File, int Line, string Key), ProfilerSqlBucket>();
    foreach (var ev in events)
    {
      var normalized = NormalizeForKey(ev.Sql);
      var key = (ev.File, ev.Line, normalized);
      if (byKey.TryGetValue(key, out var existing))
      {
        var elapsed = (long)ev.ElapsedMs;
        byKey[key] = existing with
        {
          Count = existing.Count + 1,
          TotalMs = existing.TotalMs + elapsed,
          MaxMs = Math.Max(existing.MaxMs, elapsed),
          // Keep most recent parameter sample so the hover shows the latest values.
          ParametersSample = ev.Parameters ?? existing.ParametersSample,
          SqlSample = ev.Sql,
        };
      }
      else
      {
        var elapsed = (long)ev.ElapsedMs;
        byKey[key] = new ProfilerSqlBucket(
            ComputeBucketId(ev.File, ev.Line, normalized),
            ev.File, ev.Line, ev.Sql, ev.Parameters,
            Count: 1, TotalMs: elapsed, MaxMs: elapsed);
      }
    }
    return byKey.Values.ToArray();
  }

  /// <summary>
  /// Stable identifier for a bucket — used by the server to track which buckets have already
  /// been emitted to the client, and by the client to key its own state. Identity is
  /// (file, line, normalized SQL); whitespace and literal-value differences between two
  /// otherwise-identical queries collapse onto the same id.
  /// </summary>
  public static string ComputeBucketId(string file, int line, string normalizedSql)
  {
    var input = $"{file}|{line}|{normalizedSql}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
    var sb = new StringBuilder(16);
    for (var i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
    return sb.ToString();
  }

  /// <summary>
  /// Returns true if <paramref name="frameName"/> is an EF Core or BCL frame that should be
  /// skipped when walking a sample stack to locate the user's call site. Conservative — we
  /// only skip well-known infrastructure namespaces.
  /// </summary>
  public static bool IsInfrastructureFrame(string frameName)
  {
    var bang = frameName.IndexOf('!');
    var afterBang = bang >= 0 ? frameName[(bang + 1)..] : frameName;
    return afterBang.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
        || afterBang.StartsWith("Microsoft.Data.", StringComparison.Ordinal)
        || afterBang.StartsWith("System.Data.", StringComparison.Ordinal)
        || afterBang.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
        || afterBang.StartsWith("System.Linq.", StringComparison.Ordinal)
        || afterBang.StartsWith("System.Threading.", StringComparison.Ordinal)
        || afterBang.StartsWith("System.Runtime.", StringComparison.Ordinal);
  }
}
