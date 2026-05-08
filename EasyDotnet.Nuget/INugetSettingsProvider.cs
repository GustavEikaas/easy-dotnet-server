using NuGet.Configuration;

namespace EasyDotnet.Nuget;

public interface INugetSettingsProvider
{
  ISettings GetSettings();
}