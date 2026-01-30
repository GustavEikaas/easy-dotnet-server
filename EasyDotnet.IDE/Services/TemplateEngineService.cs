using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.MsBuild;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.Utils;

namespace EasyDotnet.IDE.Services;

public class TemplateEngineService(IMsBuildService msBuildService, IClientService clientService, ISolutionService solutionService, IEditorService editorService)
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
  private const string AddToSolutionParamKey = "AddProjectToSolution";

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

    var parameters = template.ParameterDefinitions
        .Where(p => p.Precedence.PrecedenceDefinition != PrecedenceDefinition.Implicit)
        .Where(x => x.Name != TargetFrameworkOverrideParamKey)
        .Select(p =>
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
              normalizedChoices[key] = new ParameterChoice(key, choice.DisplayName ?? key);
              continue;
            }

            var uniformDisplay = $".NET {digits}";
            normalizedChoices[key] = new ParameterChoice(key, uniformDisplay);
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
        })
        .OrderByDescending(x => x.Precedence.IsRequired)
        .ToList();

    if (template.GetTemplateType() == "project" && !string.IsNullOrEmpty(clientService.ProjectInfo?.SolutionFile))
    {
      parameters.Insert(0, new TemplateParameter(
          AddToSolutionParamKey,
          type: "parameter",
          datatype: "bool",
          precedence: TemplateParameterPrecedence.Default,
          isName: false,
          defaultValue: "true",
          defaultIfOptionWithoutValue: "true",
          description: "Add the generated project to the current active solution.",
          displayName: "Add to Current Solution",
          allowMultipleValues: false,
          choices: null
      ));
    }

    return parameters;
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
    var shouldAddToSolution = false;

    if (mutableParams.ContainsKey(AddToSolutionParamKey))
    {
      if (bool.TryParse(mutableParams[AddToSolutionParamKey], out var val)) shouldAddToSolution = val;

      var clean = new Dictionary<string, string?>(mutableParams);
      clean.Remove(AddToSolutionParamKey);
      mutableParams = clean.AsReadOnly();
    }

    var result = await bootstrapper.CreateAsync(template, name, outputPath, mutableParams, cancellationToken: cancellationToken);

    if (result is null || result.Status != CreationResultStatus.Success)
    {
      throw new Exception($"Failed to instantiate template, STATUS:{result?.Status}, err:{result?.ErrorMessage ?? ""}");
    }

    if (shouldAddToSolution && !string.IsNullOrEmpty(clientService.ProjectInfo?.SolutionFile))
    {
      var projectsToAdd = result.CreationResult?.PrimaryOutputs
          .Select(x => x.Path)
          .Where(FileTypes.IsAnyProjectFile)
          .ToList() ?? [];

      if (projectsToAdd.Count == 0)
      {
        projectsToAdd = result.CreationEffects?.FileChanges
            .Select(x => Path.GetFullPath(Path.Combine(outputPath, x.TargetRelativePath)))
            .Where(FileTypes.IsAnyProjectFile)
            .ToList() ?? [];
      }

      foreach (var projectPath in projectsToAdd)
      {
        await solutionService.AddProjectToSolutionAsync(clientService.ProjectInfo.SolutionFile, projectPath, cancellationToken);
        await editorService.DisplayMessage("projectname added to solution");
      }
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
}