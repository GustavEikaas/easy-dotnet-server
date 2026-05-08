using NuGet.Configuration;

namespace EasyDotnet.Nuget;

public sealed class DefaultNugetSettingsProvider(Func<string?> rootProvider) : INugetSettingsProvider
{
  public ISettings GetSettings() => Settings.LoadDefaultSettings(root: rootProvider() ?? Directory.GetCurrentDirectory());
}