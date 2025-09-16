using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;


namespace EasyDotnet.Services.NetCoreDbg.ValueConverters;

public interface IValueConverter
{
  /// <summary>
  /// Determines if this converter applies to a given variable type.
  /// </summary>
  bool SatisfiesType(string? typeName);

  /// <summary>
  /// Converts a variable value asynchronously, possibly by calling back into the debugger.
  /// Returns a modified JsonObject representing the variable.
  /// </summary>
  Task<JsonObject> ConvertValueAsync(
      Func<int> nextSequence,
      NetcoreDbgClient client,
      JsonObject variable,
      CancellationToken cancellationToken);
}