using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.TemplateEngine.PostActionHandlers;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.Utils;
using Microsoft.VisualStudio.Threading;

namespace EasyDotnet.IDE.Services;

public class TemplateEngineService(
  IMsBuildService msBuildService,
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

  private readonly Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost _host = new(
        hostIdentifier: "easy-dotnet",
        version: "1.0.0");

  private const string FrameworkParamKey = "Framework";
  private const string TargetFrameworkOverrideParamKey = "TargetFrameworkOverride";

  public static bool IsNameRequired(string identity) => !NoNameTemplates.ContainsKey(identity);

  public async Task EnsureInstalled()
  {
    using var bootstrapper = new Bootstrapper(_host, virtualizeConfiguration: false, loadDefaultComponents: true);

    var templatesFolder = Path.Join(msBuildService.GetDotnetSdkBasePath(), "templates");
    if (!Directory.Exists(templatesFolder)) return;

    var highestVersionDir = Directory.GetDirectories(templatesFolder).ToList()
        .Select(Path.GetFileName)
        .Where(name => Version.TryParse(name, out _))
        .OrderByDescending(name => Version.Parse(name ?? ""))
        .FirstOrDefault();

    if (highestVersionDir == null) return;

    var fullPath = Path.Combine(templatesFolder, highestVersionDir);
    var nupkgs = Directory.GetFiles(fullPath, "*.nupkg");

    var existingPackageNames = new HashSet<string>(
        (await bootstrapper.GetManagedTemplatePackagesAsync(CancellationToken.None))
            .Select(x => Path.GetFileName(new Uri(x.MountPointUri).LocalPath)),
        StringComparer.OrdinalIgnoreCase
    );

    var missing = nupkgs
        .Where(x => !existingPackageNames.Contains(Path.GetFileName(x)))
        .Select(path => new InstallRequest(path))
        .ToList();

    if (missing.Count != 0)
    {
      await bootstrapper.InstallTemplatePackagesAsync(missing, InstallationScope.Global, CancellationToken.None);
    }
  }

  public async Task<List<ITemplateParameter>> GetTemplateOptions(string identity)
  {
    using var bootstrapper = new Bootstrapper(_host, virtualizeConfiguration: false, loadDefaultComponents: true);
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

    using var bootstrapper = new Bootstrapper(_host, virtualizeConfiguration: false, loadDefaultComponents: true);

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
    using var bootstrapper = new Bootstrapper(_host, virtualizeConfiguration: false, loadDefaultComponents: true);
    return await bootstrapper.GetTemplatesAsync(CancellationToken.None);
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
}