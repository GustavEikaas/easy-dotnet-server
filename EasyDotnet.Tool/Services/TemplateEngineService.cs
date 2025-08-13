using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.IDE;

namespace EasyDotnet.Services;

public class TemplateEngineService
{
  private readonly Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost host = new(
        hostIdentifier: "easy-dotnet",
        version: "1.0.0");

  private readonly string templatesRoot = @"C:\Program Files\dotnet\templates";

  public async Task EnsureInstalled()
  {
    using var bootstrapper = new Bootstrapper(
        host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);


    var highestVersionDir = Directory.GetDirectories(templatesRoot).ToList()
        .Select(Path.GetFileName)
        .Where(name => Version.TryParse(name, out _))
        .OrderByDescending(name => Version.Parse(name ?? ""))
        .FirstOrDefault();

    if (highestVersionDir == null)
    {
      return;
    }

    var fullPath = Path.Combine(templatesRoot, highestVersionDir);
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
      Console.WriteLine("Installing templates");
      var results = await bootstrapper.InstallTemplatePackagesAsync([.. missing.Select(path => new InstallRequest(path))], InstallationScope.Global, CancellationToken.None);
    }
  }

  public async Task<List<string>> GetTemplateOptions(string identity)
  {

    using var bootstrapper = new Bootstrapper(
        host,
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
        host,
        loadDefaultComponents: true,
        virtualizeConfiguration: false);
    var x = await bootstrapper.GetTemplatesAsync(CancellationToken.None);
    return x;
  }
}