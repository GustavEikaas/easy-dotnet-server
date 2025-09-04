using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.Utils;

namespace EasyDotnet.Services;

public class TemplateEngineService(MsBuildService msBuildService)
{
  private readonly Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost _host = new(
        hostIdentifier: "easy-dotnet",
        version: "1.0.0");

  private const string FrameworkParamKey = "Framework";
  private const string TargetFrameworkOverrideParamKey = "TargetFrameworkOverride";

  private static string GetTemplatesRoot() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\Program Files\dotnet\templates" : "/usr/share/dotnet/templates";

  public async Task EnsureInstalled()
  {
    using var bootstrapper = new Bootstrapper(
        _host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);


    var paths = Directory.GetDirectories(GetTemplatesRoot()).ToList().Select(Path.GetFileName);

    var highestVersionDir =
      Directory.GetDirectories(GetTemplatesRoot()).ToList()
        .Select(Path.GetFileName)
        .Where(name => Version.TryParse(name, out _))
        .OrderByDescending(name => Version.Parse(name ?? ""))
        .FirstOrDefault();

    if (highestVersionDir == null)
    {
      return;
    }

    var fullPath = Path.Combine(GetTemplatesRoot(), highestVersionDir);
    var nupkgs = Directory.GetFiles(fullPath, "*.nupkg");

    var existingPackageNames = new HashSet<string>(
        (await bootstrapper.GetManagedTemplatePackagesAsync(CancellationToken.None))
            .Select(x => Path.GetFileName(new Uri(x.MountPointUri).LocalPath)),
        StringComparer.OrdinalIgnoreCase
    );

    var missing = nupkgs
        .Where(x => !existingPackageNames.Contains(Path.GetFileName(x)))
        .ToList();

    if (missing.Count != 0)
    {
      var results = await bootstrapper.InstallTemplatePackagesAsync([.. missing.Select(path => new InstallRequest(path))], InstallationScope.Global, CancellationToken.None);
    }
  }

  public async Task<List<ITemplateParameter>> GetTemplateOptions(string identity)
  {
    using var bootstrapper = new Bootstrapper(
        _host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);
    var templates = await GetTemplatesAsync();

    var template = templates.FirstOrDefault(x => x.Identity == identity) ?? throw new Exception($"Failed to find template with id {identity}");

    var monikers = MsBuildService.QuerySdkInstallations().Select(x => x.Moniker).ToList();

    var parameters = template.ParameterDefinitions
        .Where(p => p.Precedence.PrecedenceDefinition != PrecedenceDefinition.Implicit)
        .Where(x => x.Name != TargetFrameworkOverrideParamKey)
        .Select(p =>
        {
          if (p.Name != FrameworkParamKey) return p;

          var choices = p.Choices?.ToDictionary() ?? [];
          foreach (var moniker in monikers)
          {
            if (!choices.ContainsKey(moniker))
            {
              choices[moniker] = new ParameterChoice(moniker, moniker);
            }
          }

          return new TemplateParameter(
          p.Name,
          p.Type,
          p.DataType,
          p.Precedence,
          p.IsName,
          p.DefaultValue,
          p.DefaultIfOptionWithoutValue,
          p.Description,
          p.DisplayName,
          p.AllowMultipleValues,
          choices
      );
        })
        .OrderByDescending(x => x.Precedence.IsRequired)
        .ToList();

    return parameters;
  }

  public async Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync()
  {
    using var bootstrapper = new Bootstrapper(
        _host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);
    var x = await bootstrapper.GetTemplatesAsync(CancellationToken.None);
    return x;
  }

  public async Task InstantiateTemplateAsync(string identity, string name, string outputPath, IReadOnlyDictionary<string, string?>? parameters)
  {
    var templates = await GetTemplatesAsync();

    var template = templates.FirstOrDefault(x => x.Identity == identity) ?? throw new Exception($"Failed to find template with id {identity}");

    using var bootstrapper = new Bootstrapper(
        _host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);

    var updatedParams = OverwriteTargetFrameworkIfSet(parameters);

    await bootstrapper.CreateAsync(template, name, outputPath, updatedParams);
  }

  public static IReadOnlyDictionary<string, string?> OverwriteTargetFrameworkIfSet(IReadOnlyDictionary<string, string?>? parameters)
  {
    if (parameters is null)
    {
      return new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());
    }
    var updatedParams = parameters != null
            ? new Dictionary<string, string?>(parameters)
            : [];

    if (updatedParams.TryGetValue(FrameworkParamKey, out var frameworkValue) && !string.IsNullOrWhiteSpace(frameworkValue))
    {
      updatedParams.Remove(FrameworkParamKey);
      updatedParams[TargetFrameworkOverrideParamKey] = frameworkValue;
    }
    return updatedParams.AsReadOnly();
  }
}