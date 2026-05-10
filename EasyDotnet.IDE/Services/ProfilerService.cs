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

  private sealed record ChunkResult(long TotalSamples, long Attributed, Dictionary<(string File, int Line), long> Counts);

  private async Task RunSingleShotAsync(int pid, TimeSpan duration, CancellationToken ct)
  {
    try
    {
      await notifications.NotifyProfilerState("started", $"pid={pid} mode=single-shot duration={duration.TotalSeconds:F1}s");
      var result = await CollectAndProcessChunkAsync(pid, duration, ct);
      EmitDeltas(result.Counts);
      await notifications.NotifyProfilerState("stopped",
          $"samples={result.TotalSamples} attributed={result.Attributed} buckets={result.Counts.Count}");
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
      }

      await notifications.NotifyProfilerState("stopped",
          $"chunks={chunkIndex} cumulativeSamples={totalSamplesAllChunks} buckets={totals.Count}");
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

  private async Task<ChunkResult> CollectAndProcessChunkAsync(int pid, TimeSpan duration, CancellationToken ct)
  {
    var nettrace = Path.Combine(Path.GetTempPath(), $"easydotnet-profiler-{pid}-{Guid.NewGuid():N}.nettrace");
    string? etlx = null;
    try
    {
      // 1. Collect: open EventPipe, stream to file until duration elapses or ct is cancelled,
      // then stop session and await rundown.
      var providers = new[] { new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational) };
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

      return new ChunkResult(totalSamples, attributed, counts);
    }
    finally
    {
      try { if (File.Exists(nettrace)) File.Delete(nettrace); } catch { }
      try { if (etlx != null && File.Exists(etlx)) File.Delete(etlx); } catch { }
    }
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
          (string File, int Line)? head = null;
          foreach (var sp in dbgInfo.GetSequencePoints())
          {
            if (sp.IsHidden || sp.StartLine == 0) continue;
            var doc = pdb.GetDocument(sp.Document);
            var name = pdb.GetString(doc.Name);
            if (string.IsNullOrEmpty(name)) continue;
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
    var typeHandle = method.GetDeclaringType();
    var typeDef = reader.GetTypeDefinition(typeHandle);
    var ns = reader.GetString(typeDef.Namespace);
    var typeName = reader.GetString(typeDef.Name);
    var methodName = reader.GetString(method.Name);
    var qualifiedType = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";
    return $"{qualifiedType}.{methodName}";
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
