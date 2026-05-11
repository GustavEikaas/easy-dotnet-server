using System.Collections.Concurrent;

namespace EasyDotnet.Debugger.Services;

public sealed record FrameSource(string Path, int Line);

public class FrameSourceTracker
{
  private readonly ConcurrentDictionary<int, FrameSource> _frames = new();
  private readonly ConcurrentDictionary<int, int> _frameIdByVarRef = new();
  private readonly ConcurrentDictionary<int, int> _pendingScopesByOriginalSeq = new();
  private readonly ConcurrentDictionary<int, int> _pendingVariablesByOriginalSeq = new();

  public void RecordScopesRequest(int originalSeq, int frameId)
    => _pendingScopesByOriginalSeq[originalSeq] = frameId;

  public void RecordVariablesRequest(int originalSeq, int variablesReference)
    => _pendingVariablesByOriginalSeq[originalSeq] = variablesReference;

  public int? TakeScopesFrameId(int originalSeq)
    => _pendingScopesByOriginalSeq.TryRemove(originalSeq, out var v) ? v : null;

  public int? TakeVariablesRef(int originalSeq)
    => _pendingVariablesByOriginalSeq.TryRemove(originalSeq, out var v) ? v : null;

  public void RecordFrame(int frameId, string path, int line)
    => _frames[frameId] = new FrameSource(path, line);

  public void RecordLocalsScope(int variablesReference, int frameId)
    => _frameIdByVarRef[variablesReference] = frameId;

  public FrameSource? TryGetSourceForVarRef(int variablesReference)
  {
    if (!_frameIdByVarRef.TryGetValue(variablesReference, out var frameId))
    {
      return null;
    }
    return _frames.TryGetValue(frameId, out var src) ? src : null;
  }

  public void Clear()
  {
    _frames.Clear();
    _frameIdByVarRef.Clear();
    _pendingScopesByOriginalSeq.Clear();
    _pendingVariablesByOriginalSeq.Clear();
  }
}