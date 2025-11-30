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
  bool CanConvert(VariablesResponse response);

  /// <summary>
  /// Converts the variables response to a simplified format.
  /// Should not throw - return false if conversion fails.
  /// </summary>
  Task<bool> TryConvertAsync(
    VariablesResponse response,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken);
}

public class ValueConverterService(ILogger<IValueConverter> logger)
{

  public readonly List<IValueConverter> ValueConverters = [
    new ListValueConverter(logger),
    new GuidValueConverter()
  ];

  /// <summary>
  /// Applies the first applicable converter to the response.
  /// Only one converter will be applied per response.
  /// </summary>
  public async Task<bool> TryConvertAsync(
    VariablesResponse response,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    if (response.Body?.Variables == null || response.Body.Variables.Count == 0)
    {
      return false;
    }

    foreach (var converter in ValueConverters)
    {
      if (!converter.CanConvert(response))
      {
        continue;
      }

      try
      {
        logger.LogDebug("Attempting conversion with {converterType}", converter.GetType().Name);

        var success = await converter.TryConvertAsync(response, proxy, cancellationToken);

        if (success)
        {
          logger.LogInformation(
            "Successfully converted response using {converterType}",
            converter.GetType().Name);
          return true;
        }
      }
      catch (Exception ex)
      {
        logger.LogWarning(
          ex,
          "Converter {converterType} failed, falling back to original response",
          converter.GetType().Name);
      }
    }

    return false;
  }
}