using System.Collections.Concurrent;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.ValueConverters;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Services;

/// <summary>
/// Interface for converting debugger variable representations to more user-friendly formats.
/// </summary>
public interface IValueConverter
{
  /// <summary>
  /// Determines if this converter can handle the given variables response.
  /// </summary>
  bool CanConvert(Variable val);

  /// <summary>
  /// Converts the variables response to a simplified format.
  /// Should not throw - return false if conversion fails.
  /// </summary>
  Task<VariablesResponse> TryConvertAsync(
    int id,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken);
}

public class ValueConverterService(
  ILogger<ValueConverterService> logger,
  ILoggerFactory loggerFactory)
{
  private readonly ConcurrentDictionary<long, IValueConverter> _variablesReferenceMap = new();

  public readonly List<IValueConverter> ValueConverters = [
      new DateTimeValueConverter(loggerFactory.CreateLogger<DateTimeValueConverter>()),
      new GuidValueConverter(loggerFactory.CreateLogger<GuidValueConverter>()),
      new HashSetValueConverter(loggerFactory.CreateLogger<HashSetValueConverter>()),
      new QueueValueConverter(loggerFactory.CreateLogger<QueueValueConverter>()),
      new ListValueConverter(loggerFactory.CreateLogger<ListValueConverter>()),
      new TupleValueConverter(loggerFactory.CreateLogger<TupleValueConverter>()),
      new ReadOnlyCollectionValueConverter(loggerFactory.CreateLogger<ReadOnlyCollectionValueConverter>()),
      new ConcurrentDictionaryValueConverter(loggerFactory.CreateLogger<ConcurrentDictionaryValueConverter>()),
      new DictionaryValueConverter(loggerFactory.CreateLogger<DictionaryValueConverter>()),
      new DictionaryEntryValueConverter(loggerFactory.CreateLogger<DictionaryEntryValueConverter>()),
      new ReadOnlyDictionaryValueConverter(loggerFactory.CreateLogger<ReadOnlyDictionaryValueConverter>())
    ];

  public void RegisterVariablesReferences(VariablesResponse response)
  {
    if (response.Body?.Variables == null)
      return;

    foreach (var variable in response.Body.Variables)
    {
      if (variable.VariablesReference is not int id || id <= 0)
        continue;

      var converter = ValueConverters.FirstOrDefault(c => c.CanConvert(variable));
      if (converter != null)
      {
        logger.LogDebug("[ValueConverter] added ref to {id} for {converter}", id, nameof(converter));
        _variablesReferenceMap[id] = converter;
      }
    }
  }

  public void ClearVariablesReferenceMap() => _variablesReferenceMap.Clear();

  public IValueConverter? TryGetConverterFor(int id)
  {
    _variablesReferenceMap.TryGetValue(id, out var valueConverter);
    return valueConverter;

  }
}