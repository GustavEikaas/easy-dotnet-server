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

  // method full name (as it appears in TraceEvent frame names, "Class.Method") -> (file, line)
  // Built lazily by scanning PDBs of candidate modules. For the spike we attribute at method
  // granularity (first sequence point); per-IL-offset precision is the next layer.
  private readonly ConcurrentDictionary<string, (string File, int Line)> _methodLookup = new(StringComparer.Ordinal);

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
              // No explicit transforms: rely on implicit transforms, which enumerate all
              // top-level properties of the payload (CommandExecutedEventData) by name. EF
              // surfaces the formatted SQL as a flat `LogCommandText` string — explicit nested
              // paths like `Command.CommandText` return null because the bridge can't traverse
              // DbCommand through reflection cleanly, and the bridge drops null values.
              // NOTE: do NOT add @Activity2Stop here. With that suffix the bridge emits via
              // event id 7 ("Activity2/Stop"), but TraceEvent fails to deserialize the
              // IEnumerable<KeyValuePair> Arguments payload for that event id — args arrive
              // empty. Plain event id 2 ("Event") works correctly.
              ["FilterAndPayloadSpecs"] =
                "Microsoft.EntityFrameworkCore/" +
                "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted:"
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
          if (TryResolveFrame(frameName, out var fl))
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

  // Pulls EF Core CommandExecuted events out of the trace, walks the call stack the runtime
  // captured at the moment each event fired, and produces resolved ProfilerSqlEvent records.
  // No time-window correlation — the event's own stack is the authoritative call site.
  private List<ProfilerSqlEvent> ExtractSqlEvents(TraceLog traceLog, MutableTraceEventStackSource stackSource)
  {
    var results = new List<ProfilerSqlEvent>();
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
        if (payloadEventName != "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted") continue;
        efEventsSeen++;

        var args = ev.PayloadByName("Arguments");
        if (args is not System.Collections.IEnumerable enumerable) continue;

        string? sql = null;
        string? parameters = null;
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
          if (key == "LogCommandText") sql = value;
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

        // Walk the event's own captured call stack to find the first user frame.
        var (file, line) = ResolveEventCallSite(ev, stackSource);
        if (file != "<unknown>") stackResolved++;
        results.Add(new ProfilerSqlEvent(file, line, sql!, parameters, elapsedMs));
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
  internal static void ResetSqlStackDiagCounter() => _eventStackDiagThisChunk = 0;

  // Walks the call stack captured with this event (TraceLog attaches stacks to EventPipe events
  // automatically) skipping EF/BCL infrastructure frames until we hit a frame we have PDB data
  // for. Returns ("<unknown>", 0) when the event has no stack or no user frame can be resolved.
  private (string File, int Line) ResolveEventCallSite(TraceEvent ev, MutableTraceEventStackSource stackSource)
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
      var frameName = stackSource.GetFrameName(stackSource.GetFrameIndex(stack), false);
      if (frameName.StartsWith("Thread (", StringComparison.Ordinal)) break;
      if (frameName == "CPU_TIME" || frameName == "BLOCKED_TIME" || frameName == "UNMANAGED_CODE_TIME"
          || SqlAggregator.IsInfrastructureFrame(frameName))
      {
        stack = stackSource.GetCallerIndex(stack);
        continue;
      }
      if (TryResolveFrame(frameName, out var fl)) return fl;
      stack = stackSource.GetCallerIndex(stack);
    }
    return ("<unknown>", 0);
  }

  // Frame name format from MutableTraceEventStackSource (verboseName=false) is typically:
  //   "moduleSimpleName!Namespace.Class.Method"
  // For top-level statements: "EasyDotnet.MyItemtest!Program.<<Main>$>g__HotA|0_0"
  private bool TryResolveFrame(string frameName, out (string File, int Line) fileLine)
  {
    fileLine = default;
    var bang = frameName.IndexOf('!');
    var afterBang = bang >= 0 ? frameName[(bang + 1)..] : frameName;
    // Frame names end with the method's signature, e.g. "Foo.Bar(int32, class System.String)".
    // Strip the parameter list to match our PDB key which is just "Foo.Bar".
    var paren = afterBang.IndexOf('(');
    var key = paren >= 0 ? afterBang[..paren] : afterBang;
    if (_methodLookup.TryGetValue(key, out var hit))
    {
      fileLine = hit;
      return true;
    }
    return false;
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

          // Walk sequence points, take the first non-hidden one as the method's "head" line.
          // Drop entries whose source file doesn't exist on the local filesystem: third-party
          // PDBs (NuGet packages built on other machines, source-link "/_/src/..." deterministic
          // paths) point at paths that don't resolve here. Without this we'd emit virtual text
          // requests for files the editor will never have open.
          (string File, int Line)? head = null;
          foreach (var sp in dbgInfo.GetSequencePoints())
          {
            if (sp.IsHidden || sp.StartLine == 0) continue;
            var doc = pdb.GetDocument(sp.Document);
            var name = pdb.GetString(doc.Name);
            if (string.IsNullOrEmpty(name)) continue;
            if (!File.Exists(name)) continue;
            head = (name, sp.StartLine);
            break;
          }
          if (head is null) continue;

          // Build the method's full name to match frame names.
          var fullName = BuildMethodFullName(peReader, methodDefHandle);
          if (string.IsNullOrEmpty(fullName)) continue;
          _methodLookup[fullName] = head.Value;
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
