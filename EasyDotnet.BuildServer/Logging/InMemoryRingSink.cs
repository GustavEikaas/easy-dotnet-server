namespace EasyDotnet.BuildServer.Logging;

public sealed class InMemoryRingSink(int capacity)
{
  private readonly object _gate = new();
  private readonly string[] _buffer = new string[capacity];
  private int _head;
  private int _count;

  public int Capacity { get; } = capacity;

  public void Add(string line)
  {
    if (string.IsNullOrWhiteSpace(line)) return;

    lock (_gate)
    {
      _buffer[_head] = line;
      _head = (_head + 1) % Capacity;
      if (_count < Capacity) _count++;
    }
  }

  public IReadOnlyList<string> Snapshot()
  {
    lock (_gate)
    {
      var result = new string[_count];
      var start = _count < Capacity ? 0 : _head;
      for (var i = 0; i < _count; i++)
      {
        result[i] = _buffer[(start + i) % Capacity];
      }
      return result;
    }
  }
}