using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface ISignatureHelpService
{
  SignatureHelp? GetSignatureHelp(CsprojDocument doc, int line, int character);
}

public class SignatureHelpService : ISignatureHelpService
{
  private static readonly Dictionary<(string element, string attr), (string label, string doc)> Signatures = new()
  {
    [("PackageReference", "Include")] = ("Include=\"<package id>\"", "NuGet package id (e.g. `Newtonsoft.Json`)."),
    [("PackageReference", "Version")] = ("Version=\"<version>\"", "NuGet package version (e.g. `13.0.3`). Supports floating versions like `13.*`."),
    [("PackageReference", "PrivateAssets")] = ("PrivateAssets=\"<assets>\"", "Restrict transitive flow. Common: `all`, `none`, `runtime;build`."),
    [("PackageReference", "IncludeAssets")] = ("IncludeAssets=\"<assets>\"", "Which assets from the package to consume."),
    [("PackageReference", "ExcludeAssets")] = ("ExcludeAssets=\"<assets>\"", "Assets to skip from the package."),
    [("ProjectReference", "Include")] = ("Include=\"<relative path>\"", "Relative path to a `.csproj` to reference."),
    [("ProjectReference", "PrivateAssets")] = ("PrivateAssets=\"<assets>\"", "Restrict transitive flow of the referenced project."),
    [("Reference", "Include")] = ("Include=\"<assembly>\"", "Assembly name or path to reference."),
    [("Reference", "HintPath")] = ("HintPath=\"<path>\"", "Optional path to the assembly DLL."),
    [("Import", "Project")] = ("Project=\"<path>\"", "Path to an MSBuild .props or .targets file to import."),
    [("Import", "Condition")] = ("Condition=\"<expression>\"", "Conditional import, e.g. `'$(Configuration)'=='Debug'`."),
    [("InternalsVisibleTo", "Include")] = ("Include=\"<assembly name>\"", "Assembly name granted access to internal members."),
    [("Compile", "Include")] = ("Include=\"<glob or path>\"", "Files to compile."),
    [("Compile", "Remove")] = ("Remove=\"<glob or path>\"", "Files to exclude from compilation."),
    [("Content", "Include")] = ("Include=\"<glob or path>\"", "Files to include as content."),
    [("Content", "CopyToOutputDirectory")] = ("CopyToOutputDirectory=\"<value>\"", "`Always`, `PreserveNewest`, or `Never`."),
    [("None", "Include")] = ("Include=\"<glob or path>\"", "Files included as `None`."),
    [("EmbeddedResource", "Include")] = ("Include=\"<glob or path>\"", "Files embedded as resources."),
    [("Using", "Include")] = ("Include=\"<namespace>\"", "Namespace to add as a global using."),
    [("Using", "Static")] = ("Static=\"<true|false>\"", "Whether to add as `global using static`."),
    [("Project", "Sdk")] = ("Sdk=\"<sdk id>\"", "Project SDK, e.g. `Microsoft.NET.Sdk`, `Microsoft.NET.Sdk.Web`."),
    [("Target", "Name")] = ("Name=\"<target name>\"", "Unique target name."),
    [("Target", "BeforeTargets")] = ("BeforeTargets=\"<targets>\"", "Run before the listed targets."),
    [("Target", "AfterTargets")] = ("AfterTargets=\"<targets>\"", "Run after the listed targets."),
    [("Target", "DependsOnTargets")] = ("DependsOnTargets=\"<targets>\"", "Targets that must run first."),
  };

  private static readonly (string label, string doc) ConditionSignature =
      ("Condition=\"<expression>\"", "MSBuild condition, e.g. `'$(Configuration)'=='Debug'`. Property reference: `$(Name)`. Item reference: `@(Items)`.");

  public SignatureHelp? GetSignatureHelp(CsprojDocument doc, int line, int character)
  {
    var ctx = XmlContextResolver.Resolve(doc, line, character);
    if (ctx.AttributeName == null || ctx.ElementName == null)
      return null;

    var attr = ctx.AttributeName;
    var element = ctx.ElementName;

    if (string.Equals(attr, "Condition", StringComparison.Ordinal))
      return Single(ConditionSignature.label, ConditionSignature.doc);

    if (Signatures.TryGetValue((element, attr), out var info))
      return Single(info.label, info.doc);

    return null;
  }

  private static SignatureHelp Single(string label, string doc) => new()
  {
    Signatures =
    [
      new SignatureInformation
      {
        Label = label,
        Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = doc },
      },
    ],
    ActiveSignature = 0,
    ActiveParameter = 0,
  };
}