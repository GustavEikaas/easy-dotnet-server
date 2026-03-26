using System.Text;
using System.Xml.Linq;

namespace EasyDotnet.BuildServer.MsBuildProject;

/// <summary>
/// Generates a virtual .csproj file for single-file apps with parsed directives.
/// </summary>
public static class VirtualProjectWriter
{
  /// <summary>
  /// Writes a virtual .csproj to disk and returns the path.
  /// </summary>
  public static string Write(
      string virtualDir,
      string sourceName,
      string sourceFilePath,
      List<CSharpDirectiveParser.ParsedDirective> directives)
  {
    var sdkDirectives = directives.Where(d => d.Type == "sdk").ToList();
    var primarySdk = sdkDirectives.FirstOrDefault()?.Name ?? "Microsoft.NET.Sdk";

    var children = new List<object>
    {
      new XElement("PropertyGroup",
        new XElement("IncludeProjectNameInArtifactsPaths", "false"),
        new XElement("ArtifactsPath", virtualDir)),
      SdkImport("Sdk.props", primarySdk, null)
    };

    foreach (var sdk in sdkDirectives.Skip(1))
      children.Add(SdkImport("Sdk.props", sdk.Name, sdk.Version));

    children.Add(new XElement("PropertyGroup",
        new XElement("OutputType", "Exe"),
        new XElement("TargetFramework", "net10.0"),
        new XElement("ImplicitUsings", "enable"),
        new XElement("Nullable", "enable")));

    children.Add(new XElement("PropertyGroup",
        new XElement("EnableDefaultItems", "false")));

    var userProps = directives.Where(d => d.Type == "property").ToList();
    if (userProps.Count > 0)
    {
      var propGroup = new XElement("PropertyGroup");
      foreach (var p in userProps)
        propGroup.Add(new XElement(p.Name, p.Value ?? ""));
      children.Add(propGroup);
    }

    children.Add(new XElement("PropertyGroup",
        new XElement("Features", "$(Features);FileBasedProgram")));

    var packages = directives.Where(d => d.Type == "package").ToList();
    if (packages.Count > 0)
    {
      var itemGroup = new XElement("ItemGroup");
      foreach (var p in packages)
      {
        var el = new XElement("PackageReference", new XAttribute("Include", p.Name));
        if (!string.IsNullOrEmpty(p.Version))
          el.Add(new XAttribute("Version", p.Version));
        itemGroup.Add(el);
      }
      children.Add(itemGroup);
    }

    var projects = directives.Where(d => d.Type == "project").ToList();
    if (projects.Count > 0)
    {
      var itemGroup = new XElement("ItemGroup");
      foreach (var p in projects)
        itemGroup.Add(new XElement("ProjectReference", new XAttribute("Include", p.Name)));
      children.Add(itemGroup);
    }

    children.Add(new XElement("ItemGroup",
        new XElement("Compile", new XAttribute("Include", sourceFilePath))));

    children.Add(SdkImport("Sdk.targets", primarySdk, null));
    foreach (var sdk in sdkDirectives.Skip(1))
      children.Add(SdkImport("Sdk.targets", sdk.Name, sdk.Version));

    var doc = new XDocument(new XElement("Project", [.. children]));

    var csprojPath = PathCombine(virtualDir, $"{sourceName}.csproj");
    Directory.CreateDirectory(virtualDir);

    using var writer = System.Xml.XmlWriter.Create(csprojPath, new System.Xml.XmlWriterSettings
    {
      Indent = true,
      Encoding = Encoding.UTF8
    });
    doc.Save(writer);

    return csprojPath;
  }

  private static XElement SdkImport(string project, string sdk, string? version)
  {
    var el = new XElement("Import",
        new XAttribute("Project", project),
        new XAttribute("Sdk", sdk));
    if (!string.IsNullOrEmpty(version))
      el.Add(new XAttribute("Version", version));
    return el;
  }

  private static string PathCombine(params string[] paths)
  {
#if NET5_0_OR_GREATER
    return Path.Join(paths);
#else
    return Path.Combine(paths);
#endif
  }
}