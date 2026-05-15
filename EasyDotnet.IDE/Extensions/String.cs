namespace EasyDotnet.IDE.Extensions;

public static class StringExtensions
{
  public static string OrDefault(this string? value, string defaultValue) => string.IsNullOrEmpty(defaultValue)
              ? throw new ArgumentNullException(nameof(defaultValue))
              : !string.IsNullOrWhiteSpace(value)
                  ? value
                  : defaultValue;
}