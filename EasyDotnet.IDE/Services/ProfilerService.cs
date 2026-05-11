using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public sealed record ProfilerSampleDelta(string File, int Line, long Samples, long ApproxMs);
public sealed record ProfilerStartRequest(int Pid, double? DurationSeconds = null);
public sealed record ProfilerStartResponse(int Pid, double DurationSeconds);


/// <summary>
/// Single-chunk profiler spike. Mirrors dotnet-stack report:
///   open EventPipeSession (SampleProfiler) → stream to temp .nettrace → stop after duration
///   → TraceLog → MutableTraceEventStackSource → SampleProfilerThreadTimeComputer
///   → walk samples, attribute top managed frame to (file, line) via portable PDBs.
/// Continuous profiling = wrap this in a rolling loop (next iteration of the spike).
/// </summary>
public sealed class ProfilerService(INotificationService notifications, ILogger<ProfilerService> log) : IAsyncDisposable
{
  private readonly object _gate = new();
  private bool _running;
  private CancellationTokenSource? _cts;
  private Task? _runTask;

  // method full name (as it appears in TraceEvent frame names, "Class.Method") -> sequence
  // points sorted by IL offset. At resolve time we pick the SP whose IL offset is the largest
  // value <= the frame's IL offset, which lands the attribution on the actual call line
  // (e.g. the `await db.X.ToListAsync()` line) instead of the method's opening `{`.
  private readonly ConcurrentDictionary<string, MethodLineMap> _methodLookup = new(StringComparer.Ordinal);

  private sealed record MethodLineMap(string File, (int ILOffset, int Line)[] Points);

  // Continuous mode chunk size. Each iteration: open session, collect for this duration,
  // stop, process, emit cumulative totals. ~1s of overhead per chunk for rundown+conversion.
  private static readonly TimeSpan ContinuousChunkDuration = TimeSpan.FromSeconds(2);

  public Task<ProfilerStartResponse> StartAsync(int pid, double? durationSeconds = null)
  {
    // null or non-positive -> continuous mode (rolling chunks until StopAsync).
    // positive -> single-chunk snapshot mode.
    TimeSpan? singleShot = (durationSeconds is double s && s > 0) ? TimeSpan.FromSeconds(s) : null;
    lock (_gate)
    {
      if (_running) throw new InvalidOperationException("A profiler session is already active.");
      _running = true;
      _cts = new CancellationTokenSource();
      var token = _cts.Token;
      _runTask = singleShot.HasValue
          ? Task.Run(() => RunSingleShotAsync(pid, singleShot.Value, token))
          : Task.Run(() => RunContinuousAsync(pid, ContinuousChunkDuration, token));
    }
    var reportSec = singleShot?.TotalSeconds ?? 0.0;
    log.LogInformation("Profiler started: pid={Pid} mode={Mode}", pid,
        singleShot.HasValue ? $"single-shot {reportSec:F1}s" : $"continuous {ContinuousChunkDuration.TotalSeconds:F1}s/chunk");
    return Task.FromResult(new ProfilerStartResponse(pid, reportSec));
  }

  public async Task StopAsync()
  {
    CancellationTokenSource? cts;
    Task? task;
    lock (_gate)
    {
      cts = _cts;
      task = _runTask;
      _cts = null;
      _runTask = null;
      _running = false;
    }
    cts?.Cancel();
    if (task != null)
    {
      try { await task; } catch { }
    }
    cts?.Dispose();
    _methodLookup.Clear();
  }

  private const double MsPerSample = 10.0;

  private sealed record ChunkResult(
      long TotalSamples,
      long Attributed,
      Dictionary<(string File, int Line), long> Counts,
      IReadOnlyList<ProfilerSqlBucket> SqlBuckets);


  private async Task RunSingleShotAsync(int pid, TimeSpan duration, CancellationToken ct)
  {
    try
    {
      await notifications.NotifyProfilerState("started", $"pid={pid} mode=single-shot duration={duration.TotalSeconds:F1}s");
      var result = await CollectAndProcessChunkAsync(pid, duration, ct);
      EmitDeltas(result.Counts);
      EmitSqlBuckets(result.SqlBuckets);
      await notifications.NotifyProfilerState("stopped",
          $"samples={result.TotalSamples} attributed={result.Attributed} buckets={result.Counts.Count} sql={result.SqlBuckets.Count}");
    }
    catch (OperationCanceledException)
    {
      await notifications.NotifyProfilerState("stopped", "cancelled");
    }
    catch (Exception ex)
    {
      log.LogError(ex, "Profiler single-shot failed");
      await notifications.NotifyProfilerState("error", ex.Message);
    }
    finally
    {
      lock (_gate) { _running = false; _runTask = null; _cts = null; }
    }
  }

  private async Task RunContinuousAsync(int pid, TimeSpan chunkDuration, CancellationToken ct)
  {
    var totals = new Dictionary<(string File, int Line), long>();
    var sqlTotals = new Dictionary<(string File, int Line, string Key), ProfilerSqlBucket>();
    var chunkIndex = 0;
    var totalSamplesAllChunks = 0L;
    try
    {
      await notifications.NotifyProfilerState("started",
          $"pid={pid} mode=continuous chunk={chunkDuration.TotalSeconds:F1}s");

      while (!ct.IsCancellationRequested)
      {
        chunkIndex++;
        ChunkResult chunk;
        try
        {
          chunk = await CollectAndProcessChunkAsync(pid, chunkDuration, ct);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
          // If the target process died, the next attach will fail — log and exit the loop.
          log.LogWarning(ex, "Profiler chunk {N} failed, ending continuous session", chunkIndex);
          break;
        }

        totalSamplesAllChunks += chunk.TotalSamples;
        foreach (var (k, v) in chunk.Counts)
        {
          totals.TryGetValue(k, out var c);
          totals[k] = c + v;
        }

        log.LogInformation("Profiler chunk {N}: chunkSamples={CS} chunkBuckets={CB} cumulativeSamples={TS} cumulativeBuckets={TB}",
            chunkIndex, chunk.TotalSamples, chunk.Counts.Count, totalSamplesAllChunks, totals.Count);

        if (totals.Count > 0)
        {
          EmitDeltas(totals);
        }

        foreach (var b in chunk.SqlBuckets)
        {
          var key = (b.File, b.Line, SqlAggregator.NormalizeForKey(b.SqlSample));
          if (sqlTotals.TryGetValue(key, out var existing))
          {
            sqlTotals[key] = existing with
            {
              Count = existing.Count + b.Count,
              TotalMs = existing.TotalMs + b.TotalMs,
              MaxMs = Math.Max(existing.MaxMs, b.MaxMs),
              SqlSample = b.SqlSample,
              ParametersSample = b.ParametersSample ?? existing.ParametersSample,
            };
          }
          else
          {
            sqlTotals[key] = b;
          }
        }
        if (sqlTotals.Count > 0)
        {
          EmitSqlBuckets(sqlTotals.Values.ToArray());
        }
      }

      await notifications.NotifyProfilerState("stopped",
          $"chunks={chunkIndex} cumulativeSamples={totalSamplesAllChunks} buckets={totals.Count} sql={sqlTotals.Count}");
    }
    catch (Exception ex)
    {
      log.LogError(ex, "Profiler continuous mode failed");
      await notifications.NotifyProfilerState("error", ex.Message);
    }
    finally
    {
      lock (_gate) { _running = false; _runTask = null; _cts = null; }
    }
  }

  private void EmitDeltas(Dictionary<(string File, int Line), long> counts)
  {
    var deltas = counts.Select(kv =>
        new ProfilerSampleDelta(kv.Key.File, kv.Key.Line, kv.Value, (long)(kv.Value * MsPerSample))).ToArray();
    _ = notifications.NotifyProfilerSamples(deltas);
  }

  private void EmitSqlBuckets(IReadOnlyList<ProfilerSqlBucket> buckets)
  {
    if (buckets.Count == 0) return;
    _ = notifications.NotifyProfilerSqlQueries(buckets.ToArray());
  }

  private async Task<ChunkResult> CollectAndProcessChunkAsync(int pid, TimeSpan duration, CancellationToken ct)
  {
    var nettrace = Path.Combine(Path.GetTempPath(), $"easydotnet-profiler-{pid}-{Guid.NewGuid():N}.nettrace");
    string? etlx = null;
    try
    {
      // 1. Collect: open EventPipe, stream to file until duration elapses or ct is cancelled,
      // then stop session and await rundown.
      // Two providers: the standard sample profiler for CPU stacks, plus the DiagnosticSource→
      // EventPipe bridge filtered to EF Core's CommandExecuted event. The FilterAndPayloadSpecs
      // string names the payload fields we want plucked from the DiagnosticListener payload —
      // leading "-" before a field name means "implicit (don't list)"; without it the field is
      // explicit. We list ElapsedMilliseconds explicitly because the bridge otherwise omits it.
      var providers = new[]
      {
        new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
        new EventPipeProvider(
            "Microsoft-Diagnostics-DiagnosticSource",
            EventLevel.Verbose,
            // Keywords from System.Diagnostics.DiagnosticSourceEventSource.Keywords:
            //   Events                  = 0x2    (enables FilterAndPayloadSpecs parsing)
            //   IgnoreShortCutKeywords  = 0x800  (skip auto-injected AspNetCore/EF shortcut specs)
            // We want exactly our spec, so set both. The built-in EF shortcut targets
            // BeforeExecuteCommand/AfterExecuteCommand which EF Core doesn't actually emit.
            keywords: 0x2 | 0x800,
            arguments: new Dictionary<string, string>
            {
              // Spec format documented in DiagnosticSourceEventSource.cs:
              //   Listener/EventName[@ActivityName]:[transform[;transform]*]
              // EF 9+ surfaces the formatted SQL on a flat `LogCommandText` property; EF 8 has
              // no such property and only exposes the raw `Command` (DbCommand). Listing
              // explicit transforms disables implicit enumeration, so we name every field we
              // need from BOTH shapes — fields the runtime type lacks fetch as null and the
              // bridge drops them, so the same spec covers EF 8 and EF 10.
              // NOTE: do NOT add @Activity2Stop here. With that suffix the bridge emits via
              // event id 7 ("Activity2/Stop"), but TraceEvent fails to deserialize the
              // IEnumerable<KeyValuePair> Arguments payload for that event id — args arrive
              // empty. Plain event id 2 ("Event") works correctly.
              // Two events per query:
              //   * CommandExecuting fires SYNCHRONOUSLY on the calling thread, before any
              //     await suspends — its captured stack contains the user's call site.
              //   * CommandExecuted fires when the DB reply lands, on a Npgsql/IO continuation
              //     thread — its stack has no user frame at all.
              // We correlate them by CommandId (a Guid present on both event payloads). The
              // Executing event gives us the file/line; the Executed event gives us the SQL
              // text and Duration. Specs are newline-separated per DiagnosticSourceEventSource.
              ["FilterAndPayloadSpecs"] =
                "Microsoft.EntityFrameworkCore/" +
                "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting:" +
                "CommandId=CommandId\n" +
                "Microsoft.EntityFrameworkCore/" +
                "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted:" +
                "LogCommandText=LogCommandText;" +
                "CommandText=Command.CommandText;" +
                "Duration=Duration;" +
                "CommandId=CommandId"
            })
      };
      var client = new DiagnosticsClient(pid);
      using (var session = await client.StartEventPipeSessionAsync(providers, requestRundown: true, token: ct).ConfigureAwait(false))
      using (var fs = File.Create(nettrace))
      {
        var copy = session.EventStream.CopyToAsync(fs, ct);
        try { await Task.Delay(duration, ct); }
        catch (OperationCanceledException) { }
        try { await session.StopAsync(CancellationToken.None); } catch { }
        try { await copy; } catch { }
      }

      // 2. Convert: nettrace → etlx (TraceLog).
      etlx = TraceLog.CreateFromEventPipeDataFile(nettrace);

      // 3. Process: build stack source via SampleProfilerThreadTimeComputer (canonical pattern
      // from dotnet-stack and dotnet-trace report).
      using var symbolReader = new SymbolReader(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath };
      using var traceLog = new TraceLog(etlx);
      var stackSource = new MutableTraceEventStackSource(traceLog) { OnlyManagedCodeStacks = true };
      var computer = new SampleProfilerThreadTimeComputer(traceLog, symbolReader);
      computer.GenerateThreadTimeStacks(stackSource);

      // Pre-build (and reuse across chunks) full-name → (file, line) lookup from PDBs.
      foreach (var moduleFile in traceLog.ModuleFiles)
      {
        if (string.IsNullOrEmpty(moduleFile.FilePath)) continue;
        TryIndexPdb(moduleFile.FilePath);
      }
      // Diagnostic: list non-BCL methods we indexed.
      var userMethods = _methodLookup.Keys
          .Where(k => !k.StartsWith("System.", StringComparison.Ordinal)
                   && !k.StartsWith("Microsoft.", StringComparison.Ordinal)
                   && !k.StartsWith("Internal.", StringComparison.Ordinal))
          .Take(30).ToArray();
      log.LogInformation("PDB indexed (non-BCL) methods: count={Count} sample=[{Sample}]",
          userMethods.Length, string.Join(", ", userMethods));

      // 4. Iterate samples: walk up to first non-pseudo frame, attribute to (file, line).
      var counts = new Dictionary<(string File, int Line), long>();
      var totalSamples = 0L;
      var attributed = 0L;
      stackSource.ForEach(sample =>
      {
        totalSamples++;
        var stackIndex = sample.StackIndex;
        while (stackIndex != StackSourceCallStackIndex.Invalid)
        {
          var frameIdx = stackSource.GetFrameIndex(stackIndex);
          var frameName = stackSource.GetFrameName(frameIdx, false);
          if (frameName.StartsWith("Thread (", StringComparison.Ordinal)) break;
          if (frameName == "CPU_TIME" || frameName == "BLOCKED_TIME" || frameName == "UNMANAGED_CODE_TIME")
          {
            stackIndex = stackSource.GetCallerIndex(stackIndex);
            continue;
          }
          var ilOffset = GetFrameIlOffset(stackSource, frameIdx, traceLog);
          if (TryResolveFrame(frameName, ilOffset, out var fl))
          {
            counts.TryGetValue(fl, out var c);
            counts[fl] = c + 1;
            attributed++;
            break;
          }
          stackIndex = stackSource.GetCallerIndex(stackIndex);
        }
      });

      // 5. Extract EF Core SQL events. The DiagnosticSource→EventPipe bridge captures a real
      // managed call stack on each event (no time-window correlation needed) — we walk that
      // stack via stackSource.GetCallStack and find the first user frame.
      ResetSqlStackDiagCounter();
      var sqlEvents = ExtractSqlEvents(traceLog, stackSource);
      var sqlBuckets = SqlAggregator.Aggregate(sqlEvents);

      return new ChunkResult(totalSamples, attributed, counts, sqlBuckets);
    }
    finally
    {
      try { if (File.Exists(nettrace)) File.Delete(nettrace); } catch { }
      try { if (etlx != null && File.Exists(etlx)) File.Delete(etlx); } catch { }
    }
  }

  // Pulls EF Core Command events out of the trace and produces resolved ProfilerSqlEvent records.
  //
  // Two-step correlation by CommandId:
  //   1. CommandExecuting fires synchronously on the calling thread; its captured stack has the
  //      user's call site. We resolve and stash (CommandId -> file/line) when we see it.
  //   2. CommandExecuted fires asynchronously on an IO continuation thread with no user frames
  //      on the stack but carries the SQL text and Duration. We look up the previously-stashed
  //      file/line by CommandId, then emit.
  // Falls back to walking the Executed event's own stack if no Executing event was seen for the
  // command (e.g., the trace started mid-query).
  private List<ProfilerSqlEvent> ExtractSqlEvents(TraceLog traceLog, MutableTraceEventStackSource stackSource)
  {
    var results = new List<ProfilerSqlEvent>();
    var callSiteByCommandId = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
    var efEventsSeen = 0;
    var stackResolved = 0;
    try
    {
      foreach (var ev in traceLog.Events)
      {
        if (ev.ProviderName != "Microsoft-Diagnostics-DiagnosticSource") continue;
        // The bridge writes the original DiagnosticSource event name into the payload's
        // EventName field, regardless of which EventSource event id (Event vs Activity2Stop)
        // is used on the wire. Filter on this so spec changes don't break us.
        var payloadEventName = ev.PayloadByName("EventName") as string;
        var isExecuting = payloadEventName == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting";
        var isExecuted = payloadEventName == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";
        if (!isExecuting && !isExecuted) continue;

        var args = ev.PayloadByName("Arguments");
        if (args is not System.Collections.IEnumerable enumerable) continue;

        // For CommandExecuting: capture (CommandId -> resolved call site) for later correlation.
        if (isExecuting)
        {
          string? executingCommandId = null;
          foreach (var pair in enumerable)
          {
            if (pair is not IDictionary<string, object> d) continue;
            d.TryGetValue("Key", out var kObj);
            d.TryGetValue("Value", out var vObj);
            if (kObj as string == "CommandId") { executingCommandId = vObj as string; break; }
          }
          if (string.IsNullOrEmpty(executingCommandId)) continue;
          var (file, line) = ResolveEventCallSite(ev, stackSource, traceLog);
          if (file != "<unknown>")
            callSiteByCommandId[executingCommandId!] = (file, line);
          continue;
        }

        // From here on: CommandExecuted.
        efEventsSeen++;

        string? sql = null;
        string? parameters = null;
        string? commandId = null;
        double elapsedMs = 0;
        foreach (var pair in enumerable)
        {
          // TraceEvent surfaces each KeyValuePair from the bridge's Arguments payload as a
          // DynamicTraceEventData+StructValue instance — a 2-entry IDictionary<string, object>
          // with entries "Key" -> name and "Value" -> stringified value. It implements the
          // GENERIC IDictionary<string, object>, NOT the non-generic IDictionary, so we have
          // to access via the generic interface.
          if (pair is not IDictionary<string, object> dict) continue;
          dict.TryGetValue("Key", out var keyObj);
          dict.TryGetValue("Value", out var valObj);
          var key = keyObj as string;
          var value = valObj as string;
          if (key is null) continue;
          // Top-level property names on EF Core's CommandExecutedEventData. The bridge's
          // implicit transforms surface each one with the property name as the key.
          // LogCommandText is the EF 9+ flat formatted-SQL property; CommandText comes from the
          // explicit `Command.CommandText` transform we add for EF 8 (which lacks LogCommandText).
          // Whichever the runtime emitted, take it.
          if ((key == "LogCommandText" || key == "CommandText") && !string.IsNullOrEmpty(value)) sql = value;
          else if (key == "CommandId") commandId = value;
          else if (key == "Duration" && value is not null)
          {
            // EF emits Duration as a TimeSpan formatted string e.g. "00:00:00.0030056".
            if (TimeSpan.TryParse(value, out var ts)) elapsedMs = ts.TotalMilliseconds;
            else if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var ms)) elapsedMs = ms;
          }
          // EF only includes parameter values when sensitive-data logging is enabled on the
          // DbContext. LogParameterValues is the boolean indicating that mode. We can't get
          // actual values from outside the app, so leave `parameters` null.
        }
        if (string.IsNullOrEmpty(sql)) continue;

        // Prefer the call site captured at CommandExecuting time (synchronous, user thread).
        // Fall back to walking this event's own stack only if we never saw a paired Executing
        // event for this command id.
        string resolvedFile;
        int resolvedLine;
        if (commandId is not null && callSiteByCommandId.TryGetValue(commandId, out var stashed))
        {
          resolvedFile = stashed.File;
          resolvedLine = stashed.Line;
          callSiteByCommandId.Remove(commandId);
        }
        else
        {
          (resolvedFile, resolvedLine) = ResolveEventCallSite(ev, stackSource, traceLog);
        }
        if (resolvedFile != "<unknown>") stackResolved++;
        results.Add(new ProfilerSqlEvent(resolvedFile, resolvedLine, sql!, parameters, elapsedMs));
      }
    }
    catch (Exception ex)
    {
      log.LogDebug(ex, "ExtractSqlEvents failed");
    }
    if (efEventsSeen > 0)
    {
      log.LogDebug("Profiler chunk: ef={Ef} stack-resolved={Resolved}", efEventsSeen, stackResolved);
    }
    return results;
  }

  [ThreadStatic] private static int _eventStackDiagThisChunk;
  [ThreadStatic] private static int _unresolvedStackDumpsThisChunk;
  internal static void ResetSqlStackDiagCounter()
  {
    _eventStackDiagThisChunk = 0;
    _unresolvedStackDumpsThisChunk = 0;
  }

  // Walks the call stack captured with this event (TraceLog attaches stacks to EventPipe events
  // automatically) skipping EF/BCL infrastructure frames until we hit a frame we have PDB data
  // for. Returns ("<unknown>", 0) when the event has no stack or no user frame can be resolved.
  private (string File, int Line) ResolveEventCallSite(TraceEvent ev, MutableTraceEventStackSource stackSource, TraceLog traceLog)
  {
    var traceLogStackIdx = ev.CallStackIndex();

    // Per-chunk diagnostic (first 2 events per chunk): dump whether we got a real stack and,
    // if so, the top frames so we can see why nothing resolves to user code.
    if (++_eventStackDiagThisChunk <= 2)
    {
      if (traceLogStackIdx == Microsoft.Diagnostics.Tracing.Etlx.CallStackIndex.Invalid)
      {
        log.LogInformation("SQL event has no stack (CallStackIndex=Invalid)");
      }
      else
      {
        var dumpStack = stackSource.GetCallStack(traceLogStackIdx, ev);
        var frames = new List<string>();
        while (dumpStack != StackSourceCallStackIndex.Invalid && frames.Count < 200)
        {
          frames.Add(stackSource.GetFrameName(stackSource.GetFrameIndex(dumpStack), false));
          dumpStack = stackSource.GetCallerIndex(dumpStack);
        }
        log.LogInformation("SQL event stack ({Depth} frames):\n{Frames}",
            frames.Count, string.Join("\n  ", frames));
      }
    }

    if (traceLogStackIdx == Microsoft.Diagnostics.Tracing.Etlx.CallStackIndex.Invalid)
      return ("<unknown>", 0);

    var stack = stackSource.GetCallStack(traceLogStackIdx, ev);
    while (stack != StackSourceCallStackIndex.Invalid)
    {
      var frameIdx = stackSource.GetFrameIndex(stack);
      var frameName = stackSource.GetFrameName(frameIdx, false);
      if (frameName.StartsWith("Thread (", StringComparison.Ordinal)) break;
      if (frameName == "CPU_TIME" || frameName == "BLOCKED_TIME" || frameName == "UNMANAGED_CODE_TIME"
          || SqlAggregator.IsInfrastructureFrame(frameName))
      {
        stack = stackSource.GetCallerIndex(stack);
        continue;
      }
      var ilOffset = GetFrameIlOffset(stackSource, frameIdx, traceLog);
      if (TryResolveFrame(frameName, ilOffset, out var fl)) return fl;
      stack = stackSource.GetCallerIndex(stack);
    }
    // Couldn't resolve a user frame. Dump the full stack the first few times per chunk so we
    // can see *why* (frame names that should have indexed but didn't, or stacks that bottom out
    // entirely in infrastructure / EF internals — typical for async ToListAsync continuations).
    if (++_unresolvedStackDumpsThisChunk <= 5)
    {
      var dump = stackSource.GetCallStack(traceLogStackIdx, ev);
      var frames = new List<string>();
      while (dump != StackSourceCallStackIndex.Invalid && frames.Count < 200)
      {
        frames.Add(stackSource.GetFrameName(stackSource.GetFrameIndex(dump), false));
        dump = stackSource.GetCallerIndex(dump);
      }
      log.LogInformation("UNRESOLVED SQL event stack ({Depth} frames):\n  {Frames}",
          frames.Count, string.Join("\n  ", frames));
    }
    return ("<unknown>", 0);
  }

  // Frame name format from MutableTraceEventStackSource (verboseName=false) is typically:
  //   "moduleSimpleName!Namespace.Class.Method"
  // For top-level statements: "EasyDotnet.MyItemtest!Program.<<Main>$>g__HotA|0_0"
  // For generic methods: "Namespace.Class.Method[T](args)"
  // For generic types: "Namespace.Class`1[T].Method(args)"
  private bool TryResolveFrame(string frameName, int ilOffset, out (string File, int Line) fileLine)
  {
    fileLine = default;
    var bang = frameName.IndexOf('!');
    var afterBang = bang >= 0 ? frameName[(bang + 1)..] : frameName;
    // Strip the parameter list — "Foo.Bar(int32, string)" → "Foo.Bar".
    var paren = afterBang.IndexOf('(');
    var key = paren >= 0 ? afterBang[..paren] : afterBang;
    // Strip generic instantiations the JIT/TraceEvent inserts (`[T]`, `[T1,T2]`). PDB keys are
    // built from metadata and have no instantiation suffix, so without this generic methods and
    // methods declared on generic types never match.
    key = StripGenericInstantiations(key);
    if (!_methodLookup.TryGetValue(key, out var map) || map.Points.Length == 0) return false;

    // Pick the sequence point whose IL offset is the largest value <= the frame's IL offset.
    // This is the standard PDB lookup pattern: the SP "covers" all IL up to the next SP, so the
    // last SP at-or-before the frame's offset is the source line currently executing. If we
    // don't have an IL offset (pseudo-frame, missing symbol info) fall back to the first SP,
    // which historically was the method's opening brace.
    var chosenLine = map.Points[0].Line;
    if (ilOffset >= 0)
    {
      for (var i = map.Points.Length - 1; i >= 0; i--)
      {
        if (map.Points[i].ILOffset <= ilOffset)
        {
          chosenLine = map.Points[i].Line;
          break;
        }
      }
    }
    fileLine = (map.File, chosenLine);
    return true;
  }

  // Pulls the IL offset off a stack frame by going through the TraceEventStackSource →
  // CodeAddress → TraceLog method. Returns -1 for pseudo-frames (CPU_TIME etc.) and any frame
  // the resolver couldn't attach a CodeAddress to (native, unresolved JIT).
  private static int GetFrameIlOffset(MutableTraceEventStackSource stackSource, StackSourceFrameIndex frameIdx, TraceLog traceLog)
  {
    try
    {
      var codeAddrIdx = stackSource.GetFrameCodeAddress(frameIdx);
      if (codeAddrIdx == Microsoft.Diagnostics.Tracing.Etlx.CodeAddressIndex.Invalid) return -1;
      return traceLog.CodeAddresses.ILOffset(codeAddrIdx);
    }
    catch { return -1; }
  }

  private static string StripGenericInstantiations(string name)
  {
    if (name.IndexOf('[') < 0) return name;
    var sb = new System.Text.StringBuilder(name.Length);
    var depth = 0;
    foreach (var c in name)
    {
      if (c == '[') { depth++; continue; }
      if (c == ']') { if (depth > 0) depth--; continue; }
      if (depth == 0) sb.Append(c);
    }
    return sb.ToString();
  }

  private void TryIndexPdb(string modulePath)
  {
    try
    {
      MetadataReader? pdb = null;
      MetadataReaderProvider? pdbProvider = null;
      EmbeddedPortablePdbProvider? embedded = null;

      var pdbPath = Path.ChangeExtension(modulePath, ".pdb");
      if (File.Exists(pdbPath))
      {
        var stream = File.OpenRead(pdbPath);
        pdbProvider = MetadataReaderProvider.FromPortablePdbStream(stream);
        pdb = pdbProvider.GetMetadataReader();
      }
      else if (File.Exists(modulePath))
      {
        embedded = TryOpenEmbeddedPdb(modulePath);
        pdb = embedded?.PdbReader;
      }
      if (pdb == null) return;

      // We also need the PE metadata (the .dll) to resolve method names from method tokens.
      MetadataReader? peReader = null;
      Stream? peStream = null;
      PEReader? pe = null;
      try
      {
        peStream = File.OpenRead(modulePath);
        pe = new PEReader(peStream);
        if (!pe.HasMetadata) return;
        peReader = pe.GetMetadataReader();

        foreach (var dbgHandle in pdb.MethodDebugInformation)
        {
          if (dbgHandle.IsNil) continue;
          var dbgInfo = pdb.GetMethodDebugInformation(dbgHandle);
          if (dbgInfo.SequencePointsBlob.IsNil) continue;

          var methodDefHandle = dbgHandle.ToDefinitionHandle();
          if (methodDefHandle.IsNil) continue;

          // Collect every non-hidden sequence point. We need the full table so we can map a
          // frame's IL offset onto the source line where the call site actually appears (e.g.
          // an `await ToListAsync(...)` four lines into the method body) rather than the method
          // entry. Deterministic source paths (CI, containers, `<Deterministic>true</...>`)
          // don't resolve on this machine — pick a file that exists if possible, otherwise
          // accept whatever the PDB embedded and let the editor side ignore it.
          var rawPoints = new List<(int ILOffset, int Line, string File)>();
          foreach (var sp in dbgInfo.GetSequencePoints())
          {
            if (sp.IsHidden || sp.StartLine == 0) continue;
            var doc = pdb.GetDocument(sp.Document);
            var name = pdb.GetString(doc.Name);
            if (string.IsNullOrEmpty(name)) continue;
            rawPoints.Add((sp.Offset, sp.StartLine, name));
          }
          if (rawPoints.Count == 0) continue;

          var preferredFile = rawPoints.Select(p => p.File).FirstOrDefault(File.Exists)
                              ?? rawPoints[0].File;
          // Keep only SPs from the chosen file. Mixed-file methods (partials, source-link
          // remappings) are rare; one consistent file simplifies the line-pick step.
          var points = rawPoints
              .Where(p => p.File == preferredFile)
              .OrderBy(p => p.ILOffset)
              .Select(p => (p.ILOffset, p.Line))
              .ToArray();

          // Build the method's full name to match frame names.
          var fullName = BuildMethodFullName(peReader, methodDefHandle);
          if (string.IsNullOrEmpty(fullName)) continue;
          _methodLookup[fullName] = new MethodLineMap(preferredFile, points);
        }
      }
      finally
      {
        pe?.Dispose();
        peStream?.Dispose();
        pdbProvider?.Dispose();
        embedded?.Dispose();
      }
    }
    catch (Exception ex)
    {
      log.LogDebug(ex, "PDB index failed for {Module}", modulePath);
    }
  }

  private static string BuildMethodFullName(MetadataReader reader, MethodDefinitionHandle handle)
  {
    var method = reader.GetMethodDefinition(handle);
    var qualifiedType = BuildTypeFullName(reader, method.GetDeclaringType());
    var methodName = reader.GetString(method.Name);
    return $"{qualifiedType}.{methodName}";
  }

  // Walks up nested type declarations so e.g. an async state machine
  //   `EfTarget.Runner.<RunOneAsync>d__0`
  // produces "EfTarget.Runner+<RunOneAsync>d__0" — matching the frame name format
  // TraceEvent's MutableTraceEventStackSource emits.
  private static string BuildTypeFullName(MetadataReader reader, TypeDefinitionHandle typeHandle)
  {
    var typeDef = reader.GetTypeDefinition(typeHandle);
    var typeName = reader.GetString(typeDef.Name);
    var declaringType = typeDef.GetDeclaringType();
    if (!declaringType.IsNil)
      return BuildTypeFullName(reader, declaringType) + "+" + typeName;
    var ns = reader.GetString(typeDef.Namespace);
    return string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";
  }

  private sealed class EmbeddedPortablePdbProvider : IDisposable
  {
    public MetadataReader PdbReader { get; }
    private readonly MetadataReaderProvider _provider;
    private readonly PEReader _pe;
    private readonly Stream _stream;

    public EmbeddedPortablePdbProvider(Stream stream, PEReader pe, MetadataReaderProvider provider)
    {
      _stream = stream;
      _pe = pe;
      _provider = provider;
      PdbReader = provider.GetMetadataReader();
    }

    public void Dispose()
    {
      _provider.Dispose();
      _pe.Dispose();
      _stream.Dispose();
    }
  }

  private static EmbeddedPortablePdbProvider? TryOpenEmbeddedPdb(string modulePath)
  {
    Stream? stream = null;
    PEReader? pe = null;
    try
    {
      stream = File.OpenRead(modulePath);
      pe = new PEReader(stream);
      foreach (var entry in pe.ReadDebugDirectory())
      {
        if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
        {
          var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
          return new EmbeddedPortablePdbProvider(stream, pe, provider);
        }
      }
    }
    catch { }
    pe?.Dispose();
    stream?.Dispose();
    return null;
  }

  public async ValueTask DisposeAsync() => await StopAsync();
}
