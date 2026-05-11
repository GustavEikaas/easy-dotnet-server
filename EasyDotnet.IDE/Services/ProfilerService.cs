using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public sealed record ProfilerStartRequest(int Pid, double? DurationSeconds = null);
public sealed record ProfilerStartResponse(int Pid, double DurationSeconds);

/// <summary>
/// EF-only profiler. Continuously chunks (~2 s) EventPipe captures of EF Core's DiagnosticSource
/// bridge to a temp .nettrace, converts to .etlx via TraceLog, then walks events offline —
/// correlating CommandExecuting (sync, user-thread stack) with CommandExecuted (async, carries
/// SQL + Duration) by CommandId. User call sites resolve through MutableTraceEventStackSource +
/// PDB sequence points. Coalesced buckets are emitted to the client via the dedup flush loop:
/// only changed counters are sent, and only for files the editor currently has open. Opening a
/// new buffer triggers a re-emit of its cumulative state.
///
/// We use the offline TraceLog path rather than TraceLog.CreateFromEventPipeSession (live) because
/// the real-time path silently fails to decode self-describing DiagnosticSource bridge payloads
/// in modern .NET — events arrive with empty PayloadValues. Chunk latency is ChunkDuration plus
/// ~1 s of rundown + ETLX conversion overhead.
/// </summary>
public sealed class ProfilerService(
    INotificationService notifications,
    IOpenBufferService openBuffers,
    ILogger<ProfilerService> log) : IAsyncDisposable
{
  private readonly object _gate = new();
  private bool _running;
  private CancellationTokenSource? _cts;
  private Task? _runTask;
  private EventPipeSession? _session;

  // method full name -> sequence points sorted by IL offset. Cached across the whole session.
  // Pick the SP whose IL offset is the largest value <= the frame's IL offset to land on the
  // call site rather than the method's opening `{`.
  private sealed record MethodLineMap(string File, (int ILOffset, int Line)[] Points);
  private readonly ConcurrentDictionary<string, MethodLineMap> _methodLookup = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<string, byte> _indexedModules = new(StringComparer.OrdinalIgnoreCase);

  // Bucket state — owned by the dispatch thread. We protect via _bucketsLock since the flush
  // loop runs on a separate thread.
  private readonly object _bucketsLock = new();
  private readonly Dictionary<string, ProfilerSqlBucket> _allBuckets = new(StringComparer.Ordinal);
  private readonly Dictionary<string, BucketSnapshot> _lastSent = new(StringComparer.Ordinal);
  private readonly Dictionary<string, (string File, int Line)> _callSiteByCommandId =
      new(StringComparer.Ordinal);

  private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

  private readonly record struct BucketSnapshot(long Count, long TotalMs, long MaxMs);

  public Task<ProfilerStartResponse> StartAsync(int pid, double? durationSeconds = null)
  {
    TimeSpan? singleShot = (durationSeconds is double s && s > 0) ? TimeSpan.FromSeconds(s) : null;
    lock (_gate)
    {
      if (_running) throw new InvalidOperationException("A profiler session is already active.");
      _running = true;
      _cts = new CancellationTokenSource();
      var token = _cts.Token;
      _runTask = Task.Run(() => RunAsync(pid, singleShot, token));
    }
    var reportSec = singleShot?.TotalSeconds ?? 0.0;
    log.LogInformation("Profiler started: pid={Pid} mode={Mode}", pid,
        singleShot.HasValue ? $"single-shot {reportSec:F1}s" : "continuous");
    return Task.FromResult(new ProfilerStartResponse(pid, reportSec));
  }

  public async Task StopAsync()
  {
    CancellationTokenSource? cts;
    Task? task;
    EventPipeSession? session;
    lock (_gate)
    {
      cts = _cts;
      task = _runTask;
      session = _session;
      _cts = null;
      _runTask = null;
      _session = null;
      _running = false;
    }
    // Defensive: idempotent if RunAsync's finally already ran.
    openBuffers.BufferOpened -= OnBufferOpened;
    cts?.Cancel();
    // Stopping the session is what makes TraceLogEventSource.Process() return.
    if (session != null)
    {
      try { await session.StopAsync(CancellationToken.None); } catch { }
    }
    if (task != null)
    {
      try { await task; } catch { }
    }
    cts?.Dispose();
    lock (_bucketsLock)
    {
      _allBuckets.Clear();
      _lastSent.Clear();
      _callSiteByCommandId.Clear();
    }
    _methodLookup.Clear();
    _indexedModules.Clear();
    Interlocked.Exchange(ref _diagAnyEventSeen, 0);
    Interlocked.Exchange(ref _diagDiagSourceEventSeen, 0);
    Interlocked.Exchange(ref _diagExecutingSeen, 0);
    Interlocked.Exchange(ref _diagExecutedSeen, 0);
    Interlocked.Exchange(ref _diagCallSiteResolved, 0);
    Interlocked.Exchange(ref _diagDroppedClosedBuffer, 0);
    Interlocked.Exchange(ref _diagDroppedNoCallSite, 0);
    Interlocked.Exchange(ref _diagBucketsAdded, 0);
  }

  // Chunked offline ingest: open EventPipe → write to temp .nettrace → after chunk duration,
  // stop session → convert to .etlx → walk traceLog.Events → process EF events → restart.
  // The TraceLog real-time path silently breaks payload decoding for the DS bridge in modern
  // .NET (events arrive with empty PayloadValues); the offline TraceLog has always worked.
  // Per-chunk latency = ChunkDuration + ~1s for rundown/convert.
  private static readonly TimeSpan ChunkDuration = TimeSpan.FromSeconds(2);

  private async Task RunAsync(int pid, TimeSpan? duration, CancellationToken ct)
  {
    var modeMessage = duration.HasValue
        ? $"pid={pid} mode=single-shot duration={duration.Value.TotalSeconds:F1}s"
        : $"pid={pid} mode=continuous";
    try
    {
      await notifications.NotifyProfilerState("started", modeMessage);

      // Hook buffer opens so freshly-opened files get a flush of their cumulative buckets.
      openBuffers.BufferOpened += OnBufferOpened;

      using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      var flushTask = Task.Run(() => FlushLoopAsync(flushCts.Token));

      try
      {
        if (duration.HasValue)
        {
          await ProcessChunkAsync(pid, duration.Value, ct);
        }
        else
        {
          while (!ct.IsCancellationRequested)
          {
            try { await ProcessChunkAsync(pid, ChunkDuration, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
              log.LogWarning(ex, "Profiler chunk failed; ending session");
              break;
            }
          }
        }
      }
      finally
      {
        openBuffers.BufferOpened -= OnBufferOpened;
        flushCts.Cancel();
        try { await flushTask; } catch { }
      }

      Flush(); // emit any final buckets that didn't make the last interval

      int bucketCount;
      lock (_bucketsLock) bucketCount = _allBuckets.Count;
      await notifications.NotifyProfilerState("stopped",
          ct.IsCancellationRequested ? "cancelled" : $"buckets={bucketCount}");
    }
    catch (OperationCanceledException)
    {
      await notifications.NotifyProfilerState("stopped", "cancelled");
    }
    catch (Exception ex)
    {
      log.LogError(ex, "Profiler session failed");
      try { await notifications.NotifyProfilerState("error", ex.Message); } catch { }
    }
    finally
    {
      lock (_gate)
      {
        _running = false;
        _runTask = null;
        _cts = null;
        _session = null;
      }
    }
  }

  // Diagnostic counters — interlocked so the flush thread can read consistent values without
  // taking the buckets lock. Logged periodically so it's obvious whether events are reaching us.
  private long _diagAnyEventSeen;
  private long _diagDiagSourceEventSeen;
  private long _diagExecutingSeen;
  private long _diagExecutedSeen;
  private long _diagCallSiteResolved;
  private long _diagDroppedClosedBuffer;
  private long _diagDroppedNoCallSite;
  private long _diagBucketsAdded;

  // Set only during a chunk's offline processing pass. Used by ResolveCallSite to walk the
  // stack via MutableTraceEventStackSource (the same path the original code used and that
  // works for self-describing DiagnosticSource bridge events).
  private TraceLog? _activeTraceLog;
  private MutableTraceEventStackSource? _activeStackSource;

  private async Task ProcessChunkAsync(int pid, TimeSpan duration, CancellationToken ct)
  {
    var nettrace = Path.Combine(Path.GetTempPath(), $"easydotnet-profiler-{pid}-{Guid.NewGuid():N}.nettrace");
    string? etlx = null;
    try
    {
      var providers = new[]
      {
        new EventPipeProvider(
            "Microsoft-Diagnostics-DiagnosticSource",
            EventLevel.Verbose,
            keywords: 0x2 | 0x800,
            arguments: new Dictionary<string, string>
            {
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
      EventPipeSession session;
      using (session = await client.StartEventPipeSessionAsync(providers, requestRundown: true, token: ct).ConfigureAwait(false))
      using (var fs = File.Create(nettrace))
      {
        lock (_gate) { _session = session; }
        var copy = session.EventStream.CopyToAsync(fs, ct);
        try { await Task.Delay(duration, ct); }
        catch (OperationCanceledException) { }
        try { await session.StopAsync(CancellationToken.None); } catch { }
        try { await copy; } catch { }
      }
      lock (_gate) { _session = null; }

      // Offline TraceLog: decodes self-describing DS bridge payloads correctly (the real-time
      // wrapper does not in modern .NET).
      etlx = TraceLog.CreateFromEventPipeDataFile(nettrace);
      using var symbolReader = new SymbolReader(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath };
      using var traceLog = new TraceLog(etlx);
      var stackSource = new MutableTraceEventStackSource(traceLog) { OnlyManagedCodeStacks = true };

      // Pre-index PDBs for every module loaded in this chunk. Cheap because the cache survives
      // across chunks and most modules are already indexed after the first one.
      foreach (var moduleFile in traceLog.ModuleFiles)
      {
        if (string.IsNullOrEmpty(moduleFile.FilePath)) continue;
        if (_indexedModules.TryAdd(moduleFile.FilePath, 0)) TryIndexPdb(moduleFile.FilePath);
      }

      _activeTraceLog = traceLog;
      _activeStackSource = stackSource;
      try
      {
        foreach (var ev in traceLog.Events)
        {
          OnEvent(ev);
        }
      }
      finally
      {
        _activeTraceLog = null;
        _activeStackSource = null;
      }
    }
    finally
    {
      try { if (File.Exists(nettrace)) File.Delete(nettrace); } catch { }
      try { if (etlx != null && File.Exists(etlx)) File.Delete(etlx); } catch { }
    }
  }

  private void OnEvent(TraceEvent ev)
  {
    Interlocked.Increment(ref _diagAnyEventSeen);
    if (ev.ProviderName != "Microsoft-Diagnostics-DiagnosticSource") return;
    Interlocked.Increment(ref _diagDiagSourceEventSeen);

    var payloadEventName = ev.PayloadByName("EventName") as string;
    var isExecuting = payloadEventName == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting";
    var isExecuted = payloadEventName == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";
    if (!isExecuting && !isExecuted) return;

    if (ev.PayloadByName("Arguments") is not System.Collections.IEnumerable enumerable) return;

    if (isExecuting)
    {
      Interlocked.Increment(ref _diagExecutingSeen);
      var commandId = ExtractCommandId(enumerable);
      if (string.IsNullOrEmpty(commandId)) return;

      // Always resolve and stash the call site here — the user frame is on this thread's stack.
      // We do NOT filter on open-buffer at this point; the filter is applied at emit time so
      // opening a buffer between Executing and Executed (or after) still surfaces the event.
      var (file, line) = ResolveCallSite(ev);
      if (file != "<unknown>") Interlocked.Increment(ref _diagCallSiteResolved);
      lock (_bucketsLock)
      {
        _callSiteByCommandId[commandId!] = (file, line);
      }
      return;
    }

    // CommandExecuted
    Interlocked.Increment(ref _diagExecutedSeen);
    string? sql = null;
    string? commandId2 = null;
    double elapsedMs = 0;
    foreach (var pair in enumerable)
    {
      if (pair is not IDictionary<string, object> dict) continue;
      dict.TryGetValue("Key", out var keyObj);
      dict.TryGetValue("Value", out var valObj);
      var key = keyObj as string;
      var value = valObj as string;
      if (key is null) continue;
      if ((key == "LogCommandText" || key == "CommandText") && !string.IsNullOrEmpty(value)) sql = value;
      else if (key == "CommandId") commandId2 = value;
      else if (key == "Duration" && value is not null)
      {
        if (TimeSpan.TryParse(value, out var ts)) elapsedMs = ts.TotalMilliseconds;
        else if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var ms)) elapsedMs = ms;
      }
    }
    if (string.IsNullOrEmpty(sql) || string.IsNullOrEmpty(commandId2)) return;

    (string File, int Line) callSite;
    lock (_bucketsLock)
    {
      if (!_callSiteByCommandId.Remove(commandId2!, out callSite))
      {
        Interlocked.Increment(ref _diagDroppedNoCallSite);
        return;
      }
    }

    // Capture every event regardless of buffer state. The open-buffer filter is applied at
    // emit time (Flush) so opening a buffer later re-emits the cumulative state for that file.
    // Unresolved call sites still get a bucket so we can count them under "<unknown>" if a
    // future UI wants to surface them; the flush filter just won't emit unless that file is open.
    AddToBuckets(callSite.File, callSite.Line, sql!, elapsedMs);
    Interlocked.Increment(ref _diagBucketsAdded);
  }

  private static string? ExtractCommandId(System.Collections.IEnumerable enumerable)
  {
    foreach (var pair in enumerable)
    {
      if (pair is not IDictionary<string, object> d) continue;
      d.TryGetValue("Key", out var kObj);
      d.TryGetValue("Value", out var vObj);
      if (kObj as string == "CommandId") return vObj as string;
    }
    return null;
  }

  private void AddToBuckets(string file, int line, string sql, double elapsedMs)
  {
    var normalized = SqlAggregator.NormalizeForKey(sql);
    var bucketId = SqlAggregator.ComputeBucketId(file, line, normalized);
    var elapsed = (long)elapsedMs;
    lock (_bucketsLock)
    {
      if (_allBuckets.TryGetValue(bucketId, out var existing))
      {
        _allBuckets[bucketId] = existing with
        {
          Count = existing.Count + 1,
          TotalMs = existing.TotalMs + elapsed,
          MaxMs = Math.Max(existing.MaxMs, elapsed),
          SqlSample = sql,
        };
      }
      else
      {
        _allBuckets[bucketId] = new ProfilerSqlBucket(
            BucketId: bucketId,
            File: file, Line: line,
            SqlSample: sql, ParametersSample: null,
            Count: 1, TotalMs: elapsed, MaxMs: elapsed);
      }
    }
  }

  private async Task FlushLoopAsync(CancellationToken ct)
  {
    var lastDiagLog = DateTime.UtcNow;
    try
    {
      while (!ct.IsCancellationRequested)
      {
        try { await Task.Delay(FlushInterval, ct); }
        catch (OperationCanceledException) { break; }
        Flush();

        // Unconditional heartbeat every 5s. Logging even when counts are zero is the whole
        // point — if events aren't arriving at all, we need to see "any=0 diagSource=0".
        if (DateTime.UtcNow - lastDiagLog >= TimeSpan.FromSeconds(5))
        {
          log.LogInformation(
              "Profiler heartbeat: any={Any} diagSource={Ds} executing={Executing} executed={Executed} callSiteResolved={Resolved} bucketsAdded={Added} droppedClosedBuffer={ClosedBuf} droppedNoCallSite={NoCs} openBuffers={Open}",
              Interlocked.Read(ref _diagAnyEventSeen),
              Interlocked.Read(ref _diagDiagSourceEventSeen),
              Interlocked.Read(ref _diagExecutingSeen),
              Interlocked.Read(ref _diagExecutedSeen),
              Interlocked.Read(ref _diagCallSiteResolved),
              Interlocked.Read(ref _diagBucketsAdded),
              Interlocked.Read(ref _diagDroppedClosedBuffer),
              Interlocked.Read(ref _diagDroppedNoCallSite),
              openBuffers.Snapshot().Count);
          lastDiagLog = DateTime.UtcNow;
        }
      }
    }
    catch (Exception ex)
    {
      log.LogDebug(ex, "Flush loop terminated");
    }
  }

  private void Flush()
  {
    List<ProfilerSqlBucket>? changed = null;
    lock (_bucketsLock)
    {
      foreach (var (id, bucket) in _allBuckets)
      {
        // Emit-time filter: skip buckets whose file the editor doesn't currently have open.
        // This is the only place buffer state is consulted — ingest captures everything.
        if (!openBuffers.IsOpen(bucket.File)) continue;

        var snap = new BucketSnapshot(bucket.Count, bucket.TotalMs, bucket.MaxMs);
        if (_lastSent.TryGetValue(id, out var prev) && prev == snap) continue;
        _lastSent[id] = snap;
        (changed ??= new List<ProfilerSqlBucket>()).Add(bucket);
      }
    }
    if (changed != null && changed.Count > 0)
    {
      _ = notifications.NotifyProfilerSqlQueries(changed.ToArray());
    }
  }

  // Fires when the editor opens a file. Clears any cached "last-sent" snapshots for buckets in
  // that file so the next flush re-emits the full cumulative state — the user sees the history
  // of queries that ran while the buffer was closed.
  private void OnBufferOpened(string path)
  {
    int cleared = 0;
    HashSet<string>? allFilesSnapshot = null;
    lock (_bucketsLock)
    {
      foreach (var (id, bucket) in _allBuckets)
      {
        if (string.Equals(bucket.File, path, StringComparison.Ordinal)
            || (OperatingSystem.IsWindows()
                && string.Equals(bucket.File, path, StringComparison.OrdinalIgnoreCase)))
        {
          _lastSent.Remove(id);
          cleared++;
        }
      }
      if (cleared == 0 && _allBuckets.Count > 0)
      {
        allFilesSnapshot = new HashSet<string>(_allBuckets.Values.Select(b => b.File), StringComparer.Ordinal);
      }
    }
    if (cleared > 0)
    {
      log.LogInformation("Profiler: buffer opened, cleared {N} bucket snapshot(s) for {Path}", cleared, path);
    }
    else if (allFilesSnapshot != null)
    {
      log.LogInformation(
          "Profiler: buffer opened with no matching buckets. opened={Opened} cachedFiles=[{Files}]",
          path, string.Join(" | ", allFilesSnapshot.Take(20)));
    }
  }

  // Per-chunk counter — first 2 SQL events of each chunk dump their full stack walk so we can
  // see what frames TraceLog gave us and which ones our resolver picked / skipped.
  [ThreadStatic] private static int _stackDiagThisChunk;

  // Walks the captured call stack via MutableTraceEventStackSource (offline TraceLog path).
  // Skips EF/BCL infrastructure frames; returns the first frame our PDB cache resolves.
  // ("<unknown>", 0) means no user frame on this stack — e.g. continuation thread post-await.
  private (string File, int Line) ResolveCallSite(TraceEvent ev)
  {
    var traceLog = _activeTraceLog;
    var stackSource = _activeStackSource;
    if (traceLog == null || stackSource == null) return ("<unknown>", 0);

    var traceLogStackIdx = ev.CallStackIndex();
    if (traceLogStackIdx == Microsoft.Diagnostics.Tracing.Etlx.CallStackIndex.Invalid)
      return ("<unknown>", 0);

    var diagDump = ++_stackDiagThisChunk <= 2;
    var diagLines = diagDump ? new List<string>() : null;

    var stack = stackSource.GetCallStack(traceLogStackIdx, ev);
    (string File, int Line) chosen = ("<unknown>", 0);
    while (stack != StackSourceCallStackIndex.Invalid)
    {
      var frameIdx = stackSource.GetFrameIndex(stack);
      var frameName = stackSource.GetFrameName(frameIdx, false);
      if (frameName.StartsWith("Thread (", StringComparison.Ordinal)) break;
      var isInfra = frameName == "CPU_TIME" || frameName == "BLOCKED_TIME"
          || frameName == "UNMANAGED_CODE_TIME" || SqlAggregator.IsInfrastructureFrame(frameName);
      var ilOffset = GetFrameIlOffset(stackSource, frameIdx, traceLog);

      string tag;
      if (isInfra)
      {
        tag = "infra";
      }
      else if (TryResolveFrame(frameName, ilOffset, out var fl))
      {
        tag = $"RESOLVED -> {fl.File}:{fl.Line}";
        if (chosen.File == "<unknown>") chosen = fl;
      }
      else
      {
        tag = "no-pdb-match";
      }
      diagLines?.Add($"  [{tag}] ilOffset={ilOffset} {frameName}");

      if (!diagDump && chosen.File != "<unknown>") return chosen;
      stack = stackSource.GetCallerIndex(stack);
    }

    if (diagDump && diagLines != null && diagLines.Count > 0)
    {
      log.LogInformation("Stack dump (chunk event #{N}) chose={Chose}:\n{Frames}",
          _stackDiagThisChunk, chosen.File == "<unknown>" ? "<unknown>" : $"{chosen.File}:{chosen.Line}",
          string.Join("\n", diagLines));
    }
    return chosen;
  }

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

  // Frame name format from TraceCodeAddress.FullMethodName is "Namespace.Class.Method".
  // For top-level statements: "Program.<<Main>$>g__HotA|0_0"
  // Generic methods may appear as "Namespace.Class.Method[T]".
  private bool TryResolveFrame(string frameName, int ilOffset, out (string File, int Line) fileLine)
  {
    fileLine = default;
    var bang = frameName.IndexOf('!');
    var afterBang = bang >= 0 ? frameName[(bang + 1)..] : frameName;
    var paren = afterBang.IndexOf('(');
    var key = paren >= 0 ? afterBang[..paren] : afterBang;
    // PDB keys have no instantiation suffix — strip `[T]` etc. so generics match.
    key = StripGenericInstantiations(key);
    if (!_methodLookup.TryGetValue(key, out var map) || map.Points.Length == 0) return false;

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

      Stream? peStream = null;
      PEReader? pe = null;
      try
      {
        peStream = File.OpenRead(modulePath);
        pe = new PEReader(peStream);
        if (!pe.HasMetadata) return;
        var peReader = pe.GetMetadataReader();

        foreach (var dbgHandle in pdb.MethodDebugInformation)
        {
          if (dbgHandle.IsNil) continue;
          var dbgInfo = pdb.GetMethodDebugInformation(dbgHandle);
          if (dbgInfo.SequencePointsBlob.IsNil) continue;

          var methodDefHandle = dbgHandle.ToDefinitionHandle();
          if (methodDefHandle.IsNil) continue;

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

          // Deterministic source paths (CI, containers) don't resolve on this machine — prefer
          // a file that actually exists locally, otherwise accept whatever the PDB has and let
          // the editor side ignore mismatches.
          var preferredFile = rawPoints.Select(p => p.File).FirstOrDefault(File.Exists)
                              ?? rawPoints[0].File;
          var points = rawPoints
              .Where(p => p.File == preferredFile)
              .OrderBy(p => p.ILOffset)
              .Select(p => (p.ILOffset, p.Line))
              .ToArray();

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

  // Async state machines etc. appear as nested types — produce e.g.
  // "EfTarget.Runner+<RunOneAsync>d__0" to match TraceCodeAddress.FullMethodName.
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
