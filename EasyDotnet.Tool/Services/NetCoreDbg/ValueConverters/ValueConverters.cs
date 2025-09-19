using System.Collections.Generic;

namespace EasyDotnet.Services.NetCoreDbg.ValueConverters;

public static class ValueConverters
{
  public static readonly List<IValueConverter> Converters = [new GuidValueConverter()];
}