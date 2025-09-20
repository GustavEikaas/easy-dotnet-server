using System;

namespace EasyDotnet.Extensions;

public static class StringExtensions
{
  // Double checking that is is not null on purpose.
  public static string OrDefault(this string value, string defaultValue) => string.IsNullOrEmpty(defaultValue)
              ? throw new ArgumentNullException(nameof(defaultValue))
              : !string.IsNullOrWhiteSpace(value)
                  ? value!
                  : defaultValue;
}