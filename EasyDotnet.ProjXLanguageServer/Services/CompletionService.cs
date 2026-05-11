using EasyDotnet.MsBuild;
using EasyDotnet.Nuget;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed record CompletionResult(CompletionItem[] Items, bool IsIncomplete);

public interface ICompletionService
{
  Task<CompletionResult> GetCompletionsAsync(CsprojDocument doc, int line, int character, CancellationToken cancellationToken);
}

public class CompletionService(INugetSearchService nugetSearch, ILogger<CompletionService> logger) : ICompletionService
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

  public async Task<CompletionResult> GetCompletionsAsync(CsprojDocument doc, int line, int character, CancellationToken cancellationToken)
  {
    var ctx = XmlContextResolver.Resolve(doc, line, character);

    return ctx.Kind switch
    {
      CursorContextKind.ProjectRoot => Static(GetProjectRootCompletions()),
      CursorContextKind.PropertyGroup => Static(GetPropertyGroupCompletions()),
      CursorContextKind.ItemGroup => Static(GetItemGroupCompletions()),
      CursorContextKind.InsideElementText => Static(GetInsideElementCompletions(ctx.ElementName)),
      CursorContextKind.InsideStartTag => Static(GetStartTagCompletions(ctx.ParentElementName)),
      CursorContextKind.InsideAttributeValue => await GetAttributeValueCompletionsAsync(ctx, cancellationToken),
      _ => Static([]),
    };
  }

  private static CompletionResult Static(CompletionItem[] items) => new(items, false);

  private async Task<CompletionResult> GetAttributeValueCompletionsAsync(CursorContext ctx, CancellationToken cancellationToken)
  {
    if (ctx.ElementName != "PackageReference")
    {
      return Static([]);
    }

    try
    {
      switch (ctx.AttributeName)
      {
        case "Include":
          {
            var prefix = ctx.Attributes?.GetValueOrDefault("Include") ?? string.Empty;
            if (prefix.Length < 1)
            {
              return Static([]);
            }
            var hits = await nugetSearch.SearchByPrefixAsync(prefix, take: 20, includePrerelease: false, cancellationToken);
            var items = hits.Select(h => new CompletionItem
            {
              Label = h.Id,
              Kind = CompletionItemKind.Module,
              InsertText = h.Id,
              Detail = string.IsNullOrEmpty(h.Authors) ? h.Version : $"{h.Version} — {h.Authors}",
              Documentation = string.IsNullOrEmpty(h.Description)
                ? null
                : new MarkupContent { Kind = MarkupKind.Markdown, Value = h.Description }
            }).ToArray();
            return new CompletionResult(items, IsIncomplete: true);
          }
        case "Version":
          {
            var packageId = ctx.Attributes?.GetValueOrDefault("Include");
            if (string.IsNullOrWhiteSpace(packageId))
            {
              return Static([]);
            }
            var versions = await nugetSearch.GetVersionsAsync(packageId, includePrerelease: true, cancellationToken);
            var latestStable = versions.FirstOrDefault(v => !v.IsPrerelease);
            var latestAny = versions.FirstOrDefault();

            var items = new List<CompletionItem>(versions.Count + 2);
            if (latestStable is not null)
            {
              items.Add(new CompletionItem
              {
                Label = "latest",
                Kind = CompletionItemKind.Constant,
                InsertText = latestStable.ToNormalizedString(),
                Detail = $"latest stable ({latestStable.ToNormalizedString()})",
                SortText = "00000"
              });
            }
            if (latestAny is not null && latestAny.IsPrerelease && !latestAny.Equals(latestStable))
            {
              items.Add(new CompletionItem
              {
                Label = "latest-preview",
                Kind = CompletionItemKind.Constant,
                InsertText = latestAny.ToNormalizedString(),
                Detail = $"latest prerelease ({latestAny.ToNormalizedString()})",
                SortText = "00001"
              });
            }
            items.AddRange(versions.Select((v, i) => new CompletionItem
            {
              Label = v.ToNormalizedString(),
              Kind = CompletionItemKind.Value,
              InsertText = v.ToNormalizedString(),
              Detail = v.IsPrerelease ? "prerelease" : "release",
              SortText = (i + 10).ToString("D5")
            }));
            return new CompletionResult(items.ToArray(), IsIncomplete: false);
          }
        default:
          return Static([]);
      }
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception e)
    {
      logger.LogWarning(e, "NuGet completion failed for attribute {attr}", ctx.AttributeName);
      return Static([]);
    }
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