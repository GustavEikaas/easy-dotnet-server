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
    var doc = new XDocument(
        new XElement("Project",
            BuildProperties(virtualDir),
            BuildSdkPropsImport(),
            BuildDefaultProperties(),
            BuildDefaultItemsDisable(),
            BuildPropertyDirectives(directives),
            BuildFileBasedProgramFeature(),
            BuildPackageReferences(directives),
            BuildProjectReferences(directives),
            BuildCompileItems(sourceFilePath),
            BuildSdkTargetsImport()));

    var csprojPath = PathCombine(virtualDir, $"{sourceName}.csproj");
    Directory.CreateDirectory(virtualDir);

    using (var writer = System.Xml.XmlWriter.Create(csprojPath, new System.Xml.XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
    {
      doc.Save(writer);
    }

    return csprojPath;
  }

  private static XElement BuildProperties(string virtualDir)
      => new("PropertyGroup",
          new XElement("IncludeProjectNameInArtifactsPaths", "false"),
          new XElement("ArtifactsPath", virtualDir));

  private static XElement BuildSdkPropsImport()
      => new("Import",
          new XAttribute("Project", "Sdk.props"),
          new XAttribute("Sdk", "Microsoft.NET.Sdk"));

  private static XElement BuildDefaultProperties()
      => new("PropertyGroup",
          new XElement("OutputType", "Exe"),
          new XElement("TargetFramework", "net10.0"),
          new XElement("ImplicitUsings", "enable"),
          new XElement("Nullable", "enable"),
          new XElement("PublishAot", "true"));

  private static XElement BuildDefaultItemsDisable()
      => new("PropertyGroup",
          new XElement("EnableDefaultItems", "false"));

  private static XElement BuildPropertyDirectives(List<CSharpDirectiveParser.ParsedDirective> directives)
  {
    var propGroup = new XElement("PropertyGroup");
    foreach (var directive in directives.Where(d => d.Type == "property"))
    {
      propGroup.Add(new XElement(directive.Name, directive.Value ?? ""));
    }
    return propGroup;
  }

  private static XElement BuildFileBasedProgramFeature()
      => new("PropertyGroup",
          new XElement("Features", "$(Features);FileBasedProgram"));

  private static XElement BuildPackageReferences(List<CSharpDirectiveParser.ParsedDirective> directives)
  {
    var itemGroup = new XElement("ItemGroup");
    foreach (var directive in directives.Where(d => d.Type == "package"))
    {
      var el = new XElement("PackageReference", new XAttribute("Include", directive.Name));
      if (!string.IsNullOrEmpty(directive.Version))
      {
        el.Add(new XAttribute("Version", directive.Version));
      }
      itemGroup.Add(el);
    }
    return itemGroup;
  }

  private static XElement BuildProjectReferences(List<CSharpDirectiveParser.ParsedDirective> directives)
  {
    var itemGroup = new XElement("ItemGroup");
    foreach (var directive in directives.Where(d => d.Type == "project"))
    {
      itemGroup.Add(new XElement("ProjectReference",
          new XAttribute("Include", directive.Name)));
    }
    return itemGroup;
  }

  private static XElement BuildCompileItems(string sourceFilePath)
      => new("ItemGroup",
          new XElement("Compile",
              new XAttribute("Include", sourceFilePath)));

  private static XElement BuildSdkTargetsImport()
      => new("Import",
          new XAttribute("Project", "Sdk.targets"),
          new XAttribute("Sdk", "Microsoft.NET.Sdk"));

  private static string PathCombine(params string[] paths)
  {
#if NET5_0_OR_GREATER
        return System.IO.Path.Join(paths);
#else
    return Path.Combine(paths);
#endif
  }
}
