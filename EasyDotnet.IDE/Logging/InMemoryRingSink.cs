using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace EasyDotnet.IDE.Logging;

public sealed class InMemoryRingSink(int capacity, string? outputTemplate = null) : ILogEventSink
{
  private const string DefaultOutputTemplate =
      "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

  private readonly object _gate = new();
  private readonly string[] _buffer = new string[capacity];
  private readonly ITextFormatter _formatter = new MessageTemplateTextFormatter(outputTemplate ?? DefaultOutputTemplate);
  private int _head;
  private int _count;

  public int Capacity { get; } = capacity;

  public void Emit(LogEvent logEvent)
  {
    using var writer = new StringWriter();
    _formatter.Format(logEvent, writer);
    var formatted = writer.ToString().TrimEnd('\r', '\n');

    lock (_gate)
    {
      _buffer[_head] = formatted;
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