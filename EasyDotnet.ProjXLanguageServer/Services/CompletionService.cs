using EasyDotnet.MsBuild;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface ICompletionService
{
  CompletionItem[] GetCompletions(CsprojDocument doc, int line, int character);
}

public class CompletionService : ICompletionService
{
  private static readonly Dictionary<string, string[]> ValueCompletions = new(StringComparer.Ordinal)
  {
    ["TargetFramework"] = ["net11.0", "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "netstandard2.1", "netstandard2.0", "net48", "net472"],
    ["TargetFrameworks"] = ["net9.0;net8.0", "net8.0;netstandard2.0"],
    ["Nullable"] = ["enable", "disable", "warnings", "annotations"],
    ["OutputType"] = ["Exe", "Library", "WinExe", "Module"],
    ["LangVersion"] = ["latest", "preview", "latestMajor", "14", "13", "12", "11", "10"],
    ["ImplicitUsings"] = ["enable", "disable"],
    ["TreatWarningsAsErrors"] = ["true", "false"],
    ["GenerateDocumentationFile"] = ["true", "false"],
    ["IsPackable"] = ["true", "false"],
    ["GeneratePackageOnBuild"] = ["true", "false"],
    ["PublishAot"] = ["true", "false"],
    ["PublishTrimmed"] = ["true", "false"],
    ["InvariantGlobalization"] = ["true", "false"],
    ["Configurations"] = ["Debug;Release"]
  };

  public CompletionItem[] GetCompletions(CsprojDocument doc, int line, int character)
  {
    var ctx = XmlContextResolver.Resolve(doc, line, character);

    return ctx.Kind switch
    {
      CursorContextKind.ProjectRoot => GetProjectRootCompletions(),
      CursorContextKind.PropertyGroup => GetPropertyGroupCompletions(),
      CursorContextKind.ItemGroup => GetItemGroupCompletions(),
      CursorContextKind.InsideElementText => GetInsideElementCompletions(ctx.ElementName),
      CursorContextKind.InsideStartTag => GetStartTagCompletions(ctx.ParentElementName),
      _ => [],
    };
  }

  private static CompletionItem[] GetInsideElementCompletions(string? elementName) => elementName switch
  {
    "Project" => GetProjectRootCompletions(),
    "PropertyGroup" => GetPropertyGroupCompletions(),
    "ItemGroup" => GetItemGroupCompletions(),
    _ => GetValueCompletions(elementName),
  };

  private static CompletionItem[] GetStartTagCompletions(string? parentName) => parentName switch
  {
    "Project" => GetProjectRootCompletions(),
    "PropertyGroup" => GetPropertyGroupCompletions(),
    "ItemGroup" => GetItemGroupCompletions(),
    _ => [],
  };

  private static CompletionItem[] GetValueCompletions(string? elementName)
  {
    if (elementName == null)
    {
      return [];
    }

    if (string.Equals(elementName, "UserSecretsId", StringComparison.Ordinal))
    {
      return
      [
        new CompletionItem
        {
          Label = "new-guid",
          Kind = CompletionItemKind.Value,
          InsertText = Guid.NewGuid().ToString(),
          Detail = "Generate a new UserSecretsId GUID"
        }
      ];
    }

    if (!ValueCompletions.TryGetValue(elementName, out var values))
    {
      return [];
    }

    return [.. values.Select(v => new CompletionItem
    {
      Label = v,
      Kind = CompletionItemKind.Value,
      InsertText = v,
      Detail = $"{elementName} value"
    })];
  }

  private static CompletionItem[] GetProjectRootCompletions() =>
  [
    new CompletionItem { Label = "PropertyGroup", Kind = CompletionItemKind.Class, InsertText = "PropertyGroup>\n  $0\n</PropertyGroup>", InsertTextFormat = InsertTextFormat.Snippet, Detail = "MSBuild PropertyGroup" },
    new CompletionItem { Label = "ItemGroup", Kind = CompletionItemKind.Class, InsertText = "ItemGroup>\n  $0\n</ItemGroup>", InsertTextFormat = InsertTextFormat.Snippet, Detail = "MSBuild ItemGroup" },
    new CompletionItem { Label = "Target", Kind = CompletionItemKind.Class, InsertText = "Target Name=\"$1\">\n  $0\n</Target>", InsertTextFormat = InsertTextFormat.Snippet, Detail = "MSBuild Target" },
    new CompletionItem { Label = "Import", Kind = CompletionItemKind.Class, InsertText = "Import Project=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "MSBuild Import" },
    new CompletionItem { Label = "Choose", Kind = CompletionItemKind.Class, InsertText = "Choose>\n  <When Condition=\"$1\">\n    $0\n  </When>\n</Choose>", InsertTextFormat = InsertTextFormat.Snippet, Detail = "MSBuild Choose/When block" },
  ];

  private static CompletionItem[] GetPropertyGroupCompletions() =>
  [
    .. MsBuildProperties.GetAllPropertiesWithDocs().Select(p => new CompletionItem
    {
      Label = p.Name,
      Kind = CompletionItemKind.Property,
      InsertText = $"{p.Name}>$0</{p.Name}>",
      InsertTextFormat = InsertTextFormat.Snippet,
      Detail = "MSBuild Property",
      Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = p.Description }
    })
  ];

  private static CompletionItem[] GetItemGroupCompletions() =>
  [
    new CompletionItem { Label = "PackageReference", Kind = CompletionItemKind.Class, InsertText = "PackageReference Include=\"$1\" Version=\"$2\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "NuGet Package Reference", Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = "Reference to a NuGet package" } },
    new CompletionItem { Label = "ProjectReference", Kind = CompletionItemKind.Class, InsertText = "ProjectReference Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Project Reference", Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = "Reference to another project in the solution" } },
    new CompletionItem { Label = "Reference", Kind = CompletionItemKind.Class, InsertText = "Reference Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Assembly Reference" },
    new CompletionItem { Label = "Compile", Kind = CompletionItemKind.Class, InsertText = "Compile Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Compile Item" },
    new CompletionItem { Label = "None", Kind = CompletionItemKind.Class, InsertText = "None Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "None Item" },
    new CompletionItem { Label = "Content", Kind = CompletionItemKind.Class, InsertText = "Content Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Content Item" },
    new CompletionItem { Label = "EmbeddedResource", Kind = CompletionItemKind.Class, InsertText = "EmbeddedResource Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Embedded Resource" },
    new CompletionItem { Label = "Using", Kind = CompletionItemKind.Class, InsertText = "Using Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Global Using directive" },
    new CompletionItem { Label = "InternalsVisibleTo", Kind = CompletionItemKind.Class, InsertText = "InternalsVisibleTo Include=\"$1\" />", InsertTextFormat = InsertTextFormat.Snippet, Detail = "InternalsVisibleTo assembly" },
  ];
}