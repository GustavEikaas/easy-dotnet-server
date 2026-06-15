namespace EasyDotnet.Debugger;

/// <summary>
/// Polyfill feature flags — each flag represents an additional interception the proxy performs
/// because the underlying debugger lacks the capability. The base proxy (TCP bridging and
/// init request rewriting) is always active and is not controlled by these flags.
/// </summary>
public sealed record DebuggerProxyFeatures
{
  /// <summary>
  /// Proxy intercepts the client attach request, spawns the user process via startup hook,
  /// sends <c>runInTerminal</c> to the client itself, then rewrites to <c>attach</c>+PID.
  /// When false the strategy rewrites to a <c>launch</c> request and the debugger handles
  /// <c>runInTerminal</c> natively.
  /// </summary>
  public bool InterceptRunInTerminal { get; init; } = false;

  /// <summary>
  /// Proxy injects <c>supportsCompletionsRequest=true</c> into the <c>initialize</c> response
  /// because the debugger does not advertise the capability itself.
  /// </summary>
  public bool AdvertiseCompletions { get; init; } = false;

  /// <summary>
  /// Proxy rewrites REPL <c>evaluate</c> assignment expressions (e.g. <c>x = 5</c>) into
  /// <c>setExpression</c> requests because the debugger requires it.
  /// </summary>
  public bool RewriteEvaluateAssignments { get; init; } = false;

  /// <summary>
  /// Proxy emits CPU / memory telemetry events to the client.
  /// </summary>
  public bool EmitTelemetry { get; init; } = false;

  /// <summary>
  /// Proxy decorates variable responses with Roslyn-resolved source locations.
  /// </summary>
  public bool DecorateVariableLocations { get; init; } = false;

  /// <summary>
  /// The engine is compatible with the proxy's value-converter pretty-printing.
  /// When true, <c>DebuggerOptions.ApplyValueConverters</c> from the client is respected;
  /// when false, value converters are never applied regardless of the client setting.
  /// </summary>
  public bool SupportsValueConverters { get; init; } = false;

  public static readonly DebuggerProxyFeatures None = new();

  public static readonly DebuggerProxyFeatures NetCoreDbgDefaults = new()
  {
    InterceptRunInTerminal = true,
    AdvertiseCompletions = true,
    RewriteEvaluateAssignments = true,
    EmitTelemetry = true,
    DecorateVariableLocations = true,
    SupportsValueConverters = true,
  };

  public static readonly DebuggerProxyFeatures SharpDbgDefaults = new()
  {
    InterceptRunInTerminal = false,
    AdvertiseCompletions = false,
    RewriteEvaluateAssignments = false,
    EmitTelemetry = true,
    DecorateVariableLocations = true,
    SupportsValueConverters = false,
  };
}