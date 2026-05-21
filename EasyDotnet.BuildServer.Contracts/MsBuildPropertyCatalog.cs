using System.Reflection;

namespace EasyDotnet.BuildServer.Contracts;

public sealed record MsBuildPropertyInfo(string Name, string Description);

public static class MsBuildPropertyCatalog
{
  private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
  {
    ["TargetFramework"] = "Specifies the target framework moniker (TFM) for the project, such as net8.0 or net6.0-windows.",
    ["TargetFrameworks"] = "Specifies multiple target framework monikers (TFMs) for multi-targeted projects, separated by semicolons.",
    ["Nullable"] = "Whether nullable reference types are enabled.",
    ["OutputType"] = "Specifies the type of build output to generate, such as Exe, Library, or WinExe.",
    ["LangVersion"] = "Specifies the C# language version used by the project.",
    ["ImplicitUsings"] = "Controls whether SDK-style projects generate implicit global using directives.",
    ["TreatWarningsAsErrors"] = "Controls whether compiler warnings are treated as build errors.",
    ["GeneratePackageOnBuild"] = "Controls whether a NuGet package is generated when the project builds.",
    ["IsPackable"] = "Indicates whether the project can be packed into a NuGet package.",
    ["PackageId"] = "Specifies the NuGet package identifier.",
    ["Version"] = "Specifies the project or package version.",
    ["UserSecretsId"] = "Specifies the user secrets identifier for the project."
  };

  public static IEnumerable<string> GetAllPropertyNames() => GetAllPropertiesWithDocs().Select(p => p.Name);

  public static IEnumerable<MsBuildPropertyInfo> GetAllPropertiesWithDocs() =>
      typeof(DotnetProject)
          .GetProperties(BindingFlags.Public | BindingFlags.Instance)
          .Select(p => p.Name)
          .Distinct(StringComparer.Ordinal)
          .OrderBy(name => name, StringComparer.Ordinal)
          .Select(name => new MsBuildPropertyInfo(
              name,
              Descriptions.TryGetValue(name, out var description)
                  ? description
                  : $"MSBuild property `{name}`."));
}