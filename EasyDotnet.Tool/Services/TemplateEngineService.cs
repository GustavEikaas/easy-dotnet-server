using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.Utils;

namespace EasyDotnet.Services;

public class TemplateEngineService
{
  private readonly Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost _host = new(
        hostIdentifier: "easy-dotnet",
        version: "1.0.0");

  private static readonly string TemplatesRoot = @"C:\Program Files\dotnet\templates";

  public async Task EnsureInstalled()
  {
    using var bootstrapper = new Bootstrapper(
        _host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);


    var paths = Directory.GetDirectories(TemplatesRoot).ToList()
      .Select(Path.GetFileName);


    foreach (var x in paths)
    {
      var fullPath = Path.Combine(TemplatesRoot, x!);
      var nupkgs = Directory.GetFiles(fullPath, "*.nupkg");
      var results = await bootstrapper.InstallTemplatePackagesAsync([.. nupkgs.Select(path => new InstallRequest(path))], InstallationScope.Global, CancellationToken.None);
    }

    // .ForEach(async x =>
    // {
    //
    //   var fullPath = Path.Combine(TemplatesRoot, x!);
    //   var nupkgs = Directory.GetFiles(fullPath, "*.nupkg");
    //   var results = await bootstrapper.InstallTemplatePackagesAsync([.. nupkgs.Select(path => new InstallRequest(path))], InstallationScope.Global, CancellationToken.None);
    // });


    // var highestVersionDir =
    //   Directory.GetDirectories(TemplatesRoot).ToList()
    //     .Select(Path.GetFileName)
    //     .Where(name => Version.TryParse(name, out _))
    //     .OrderByDescending(name => Version.Parse(name ?? ""))
    //     .FirstOrDefault();
    //
    // if (highestVersionDir == null)
    // {
    //   return;
    // }
    //
    // var fullPath = Path.Combine(TemplatesRoot, highestVersionDir);
    // var nupkgs = Directory.GetFiles(fullPath, "*.nupkg");
    //
    // var existingPackageNames = new HashSet<string>(
    //     (await bootstrapper.GetManagedTemplatePackagesAsync(CancellationToken.None))
    //         .Select(x => Path.GetFileName(new Uri(x.MountPointUri).LocalPath)),
    //     StringComparer.OrdinalIgnoreCase
    // );
    //
    // var missing = nupkgs
    //     .Where(x => !existingPackageNames.Contains(Path.GetFileName(x)))
    //     .ToList();
    //
    // if (missing.Count != 0)
    // {
    //   var results = await bootstrapper.InstallTemplatePackagesAsync([.. missing.Select(path => new InstallRequest(path))], InstallationScope.Global, CancellationToken.None);
    // }
  }

  public async Task<List<ITemplateParameter>> GetTemplateOptions(string identity)
  {
    using var bootstrapper = new Bootstrapper(
        _host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);
    var templates = await GetTemplatesAsync();

    var template = templates.FirstOrDefault(x => x.Identity == identity) ?? throw new Exception($"Failed to find template with id {identity}");

    var parameters = template.ParameterDefinitions.Where(x => x.Precedence.PrecedenceDefinition != PrecedenceDefinition.Implicit).ToList();
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
    await bootstrapper.CreateAsync(template, name, outputPath, parameters ?? new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>()));
  }
}