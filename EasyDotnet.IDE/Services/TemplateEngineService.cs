using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.TemplateEngine.PostActionHandlers;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.Utils;
using NuGet.Packaging;
using NuGet.Versioning;

namespace EasyDotnet.IDE.Services;

public class TemplateEngineService(
  Bootstrapper bootstrapper,
  IMsBuildService msBuildService,
  ILogger<TemplateEngineService> logger,
  PostActionProcessor postActionProcessor)
{
  private static readonly Dictionary<string, string> NoNameTemplates = new(StringComparer.OrdinalIgnoreCase)
  {
    ["Microsoft.Standard.QuickStarts.DirectoryProps"] = "Directory.Build.props",
    ["Microsoft.Standard.QuickStarts.DirectoryTargets"] = "Directory.Build.targets",
    ["Microsoft.Standard.QuickStarts.DirectoryPackages"] = "Directory.Packages.props",
    ["Microsoft.Standard.QuickStarts.EditorConfigFile"] = ".editorconfig",
    ["Microsoft.Standard.QuickStarts.GitignoreFile"] = ".gitignore",
    ["Microsoft.Standard.QuickStarts.GlobalJsonFile"] = "global.json",
    ["Microsoft.Standard.QuickStarts.Nuget.Config"] = "NuGet.Config",
    ["Microsoft.Standard.QuickStarts.Web.Config"] = "web.config",
    ["Microsoft.Standard.QuickStarts.ToolManifestFile"] = "dotnet-tools.json"
  };


  private const string FrameworkParamKey = "Framework";
  private const string TargetFrameworkOverrideParamKey = "TargetFrameworkOverride";

  public static bool IsNameRequired(string identity) => !NoNameTemplates.ContainsKey(identity);

  public async Task EnsureInstalled()
  {
    var templatesFolder = Path.Join(msBuildService.GetDotnetSdkBasePath(), "templates");
    if (!Directory.Exists(templatesFolder)) return;

    var highestVersionDir = Directory.GetDirectories(templatesFolder)
        .Select(Path.GetFileName)
        .Where(n => Version.TryParse(n, out _))
        .OrderByDescending(n => Version.Parse(n!))
        .FirstOrDefault();

    if (highestVersionDir == null) return;
    var searchPath = Path.Combine(templatesFolder, highestVersionDir);

    var localPackages = Directory.GetFiles(searchPath, "*.nupkg")
        .Select(GetPackageMetadata)
        .Where(m => m != null)
        .GroupBy(p => p!.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.OrderByDescending(p => p!.Version).First())
        .ToList();

    var installedPackages = await bootstrapper.GetManagedTemplatePackagesAsync(CancellationToken.None);

    var installRequests = new List<InstallRequest>();
    var uninstallRequests = new List<IManagedTemplatePackage>();

    foreach (var target in localPackages)
    {
      var installedInstances = installedPackages
          .Where(p => string.Equals(p.Identifier, target!.Id, StringComparison.OrdinalIgnoreCase))
          .ToList();

      var isTargetVersionInstalled = false;

      foreach (var installed in installedInstances)
      {
        if (!NuGetVersion.TryParse(installed.Version, out var installedVer))
        {
          uninstallRequests.Add(installed);
          continue;
        }

        if (installedVer == target!.Version)
        {
          if (isTargetVersionInstalled) uninstallRequests.Add(installed); // Duplicate
          else isTargetVersionInstalled = true;
        }
        else
        {
          uninstallRequests.Add(installed);
        }
      }

      if (!isTargetVersionInstalled)
      {
        installRequests.Add(new InstallRequest(target!.FullPath));
      }
    }

    if (uninstallRequests.Count != 0)
    {
      await bootstrapper.UninstallTemplatePackagesAsync(uninstallRequests, CancellationToken.None);
      logger.LogInformation("Uninstalled {count} outdated packages.", uninstallRequests.Count);
    }

    if (installRequests.Count != 0)
    {
      await bootstrapper.InstallTemplatePackagesAsync(installRequests, InstallationScope.Global, CancellationToken.None);
      logger.LogInformation("Installed {count} new packages.", installRequests.Count);
    }
  }

  public async Task<List<ITemplateParameter>> GetTemplateOptions(string identity)
  {
    var templates = await GetTemplatesAsync();
    var template = templates.FirstOrDefault(x => x.Identity == identity)
                       ?? throw new Exception($"Failed to find template with id {identity}");

    var monikers = msBuildService.QuerySdkInstallations().Select(x => x.Moniker).ToList();

    return [.. template.ParameterDefinitions
            .Where(p => p.Precedence.PrecedenceDefinition != PrecedenceDefinition.Implicit)
            .Where(x => x.Name != TargetFrameworkOverrideParamKey)
            .Select(p => NormalizeFrameworkParameter(p, monikers))
            .OrderByDescending(x => x.Precedence.IsRequired)];
  }

  public async Task<ITemplateCreationResult> InstantiateTemplateAsync(string identity, string name, string outputPath, IReadOnlyDictionary<string, string?>? parameters, CancellationToken cancellationToken)
  {
    var templates = await GetTemplatesAsync();
    var template = templates.FirstOrDefault(x => x.Identity == identity) ?? throw new Exception($"Failed to find template with id {identity}");

    if (string.IsNullOrWhiteSpace(name) && NoNameTemplates.TryGetValue(identity, out var defaultName))
    {
      name = defaultName;
    }

    var mutableParams = OverwriteTargetFrameworkIfSet(parameters);

    var result = await bootstrapper.CreateAsync(template, name, outputPath, mutableParams, cancellationToken: cancellationToken);

    if (result is null || result.Status != CreationResultStatus.Success)
    {
      throw new Exception($"Failed to instantiate template, STATUS:{result?.Status}, err:{result?.ErrorMessage ?? ""}");
    }

    if (result.CreationResult?.PostActions is not null)
    {
      await postActionProcessor.ProcessAsync(result.CreationResult.PostActions, result.CreationResult.PrimaryOutputs, outputPath, cancellationToken);
    }

    return result;
  }

  public async Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync()
  {
    var allTemplates = await bootstrapper.GetTemplatesAsync(CancellationToken.None);
    return [.. allTemplates.Where(t => t.MountPointUri.Contains(".templateengine", StringComparison.OrdinalIgnoreCase))];
  }

  public static string? GetBestEntryPoint(ITemplateCreationResult result, string outputPath)
  {
    var outputs = result?.CreationResult?.PrimaryOutputs.Select(x => x.Path).ToList() ?? [];

    var program = outputs.FirstOrDefault(x => Path.GetFileName(x).Equals("Program.cs", StringComparison.OrdinalIgnoreCase));
    if (program != null) return program;

    var fsProgram = outputs.FirstOrDefault(x => Path.GetFileName(x).Equals("Program.fs", StringComparison.OrdinalIgnoreCase));
    if (fsProgram != null) return fsProgram;

    var anyCs = outputs.FirstOrDefault(x => x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
    if (anyCs != null) return anyCs;

    if (Directory.Exists(outputPath))
    {
      return Directory.EnumerateFiles(outputPath, "Program.cs", SearchOption.AllDirectories).FirstOrDefault()
          ?? Directory.EnumerateFiles(outputPath, "*.cs", SearchOption.AllDirectories).FirstOrDefault();
    }

    return null;
  }

  public static IReadOnlyDictionary<string, string?> OverwriteTargetFrameworkIfSet(IReadOnlyDictionary<string, string?>? parameters)
  {
    if (parameters is null) return new Dictionary<string, string?>();
    var updatedParams = new Dictionary<string, string?>(parameters);

    if (updatedParams.TryGetValue(FrameworkParamKey, out var frameworkValue) && !string.IsNullOrWhiteSpace(frameworkValue))
    {
      updatedParams.Remove(FrameworkParamKey);
      updatedParams[TargetFrameworkOverrideParamKey] = frameworkValue;
    }
    return updatedParams.AsReadOnly();
  }

  private static ITemplateParameter NormalizeFrameworkParameter(ITemplateParameter p, List<string> monikers)
  {
    if (p.Name != FrameworkParamKey) return p;

    var rawChoices = p.Choices?.ToDictionary(k => k.Key, v => v.Value) ?? [];

    foreach (var moniker in monikers)
    {
      if (!rawChoices.ContainsKey(moniker))
      {
        rawChoices[moniker] = new ParameterChoice(moniker, moniker);
      }
    }

    var normalizedChoices = new Dictionary<string, ParameterChoice>();
    foreach (var (key, choice) in rawChoices)
    {
      var digits = new string([.. key.Where(c => char.IsDigit(c) || c == '.')]);
      digits = digits.Trim('.');

      if (string.IsNullOrWhiteSpace(digits))
      {
        var existingDisplay = choice.DisplayName ?? key;
        normalizedChoices[key] = new ParameterChoice(existingDisplay, existingDisplay);
        continue;
      }

      var uniformDisplay = $".NET {digits}";

      normalizedChoices[key] = new ParameterChoice(uniformDisplay, uniformDisplay);
    }

    var sortedChoices = normalizedChoices
        .OrderByDescending(kvp =>
        {
          var vString = new string([.. kvp.Key.Where(c => char.IsDigit(c) || c == '.')]);
          return Version.TryParse(vString, out var v) ? v : new Version(0, 0);
        })
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    return new TemplateParameter(
        p.Name, p.Type, p.DataType, p.Precedence, p.IsName,
        p.DefaultValue, p.DefaultIfOptionWithoutValue, p.Description,
        p.DisplayName, p.AllowMultipleValues, sortedChoices
    );
  }

  private record PackageInfo(string Id, NuGetVersion Version, string FullPath);

  private static PackageInfo? GetPackageMetadata(string nupkgPath)
  {
    try
    {
      using var reader = new PackageArchiveReader(nupkgPath);
      var nuspec = reader.NuspecReader;
      return new PackageInfo(nuspec.GetId(), nuspec.GetVersion(), nupkgPath);
    }
    catch
    {
      return null;
    }
  }

}