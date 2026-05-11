using System.Collections.Concurrent;
using System.Text.Json;
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
      new DateTimeOffsetValueConverter(loggerFactory.CreateLogger<DateTimeOffsetValueConverter>()),
      new GuidValueConverter(loggerFactory.CreateLogger<GuidValueConverter>()),
      new HashSetValueConverter(loggerFactory.CreateLogger<HashSetValueConverter>()),
      new QueueValueConverter(loggerFactory.CreateLogger<QueueValueConverter>()),
      new ListValueConverter(loggerFactory.CreateLogger<ListValueConverter>()),
      new TupleValueConverter(loggerFactory.CreateLogger<TupleValueConverter>()),
      new ReadOnlyCollectionValueConverter(loggerFactory.CreateLogger<ReadOnlyCollectionValueConverter>()),
      new ConcurrentDictionaryValueConverter(loggerFactory.CreateLogger<ConcurrentDictionaryValueConverter>()),
      new DictionaryValueConverter(loggerFactory.CreateLogger<DictionaryValueConverter>()),
      new DictionaryEntryValueConverter(loggerFactory.CreateLogger<DictionaryEntryValueConverter>()),
      new ReadOnlyDictionaryValueConverter(loggerFactory.CreateLogger<ReadOnlyDictionaryValueConverter>()),
      new VersionValueConverter(loggerFactory.CreateLogger<VersionValueConverter>()),
      new CancellationTokenValueConverter(loggerFactory.CreateLogger<CancellationTokenValueConverter>()),
      new CancellationTokenSourceValueConverter(loggerFactory.CreateLogger<CancellationTokenSourceValueConverter>()),
      new TimeSpanValueConverter(loggerFactory.CreateLogger<TimeSpanValueConverter>()),
      new TimeOnlyValueConverter(loggerFactory.CreateLogger<TimeOnlyValueConverter>()),
      new DateOnlyValueConverter(loggerFactory.CreateLogger<DateOnlyValueConverter>()),
      new StopwatchValueConverter(loggerFactory.CreateLogger<StopwatchValueConverter>()),
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

  public void FormatInlineStringJsonVariables(VariablesResponse response)
  {
    if (response.Body?.Variables == null)
    {
      return;
    }

    foreach (var variable in response.Body.Variables.Where(x => x.Type.Equals("string", StringComparison.OrdinalIgnoreCase)))
    {
      if (JsonStringFormatter.TryFormatJsonSingleLine(variable.Value, out var compactJson))
      {
        variable.Value = compactJson;
      }
    }
  }

  public void FormatEvaluateResponse(Response response)
  {
    if (!response.Success || !string.Equals(response.Command, "evaluate", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    if (response.Body is null || response.Body.Value.ValueKind != JsonValueKind.Object)
    {
      return;
    }

    if (!response.Body.Value.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.String)
    {
      return;
    }

    var rawResult = resultElement.GetString();
    if (string.IsNullOrWhiteSpace(rawResult) || !JsonStringFormatter.TryFormatJson(rawResult, out var prettyJson))
    {
      return;
    }

    var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(response.Body.Value.GetRawText());
    if (body == null)
    {
      return;
    }

    body["result"] = JsonSerializer.SerializeToElement(prettyJson);
    response.Body = JsonSerializer.SerializeToElement(body);
  }

  public void ClearVariablesReferenceMap() => _variablesReferenceMap.Clear();

  public IValueConverter? TryGetConverterFor(int id)
  {
    _variablesReferenceMap.TryGetValue(id, out var valueConverter);
    return valueConverter;

  }
}