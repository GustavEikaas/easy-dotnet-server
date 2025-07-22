using System;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EasyDotnet.Services;

public class RoslynProjectMetadataCache
{
  private readonly ConcurrentDictionary<string, ProjectCacheItem> _cache = new();

  public bool TryGet(string projectPath, out ProjectCacheItem? item) => _cache.TryGetValue(projectPath, out item);

  public void Set(string projectPath, Project project)
  {
    ArgumentNullException.ThrowIfNull(project);

    var rootNamespace = project.DefaultNamespace;
    if (string.IsNullOrEmpty(rootNamespace))
    {
      throw new Exception("root namespace cannot be null");
    }

    var parseOptions = project.ParseOptions as CSharpParseOptions;
    var langVersion = parseOptions?.LanguageVersion ?? LanguageVersion.CSharp9;

    _cache[projectPath] = new ProjectCacheItem(rootNamespace, langVersion);
  }
}

public record ProjectCacheItem(string RootNamespace, LanguageVersion LanguageVersion)
{
  public bool SupportsFileScopedNamespace => LanguageVersion >= LanguageVersion.CSharp10;
}