using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyDotnet.IDE.Upgrade.Models;
using EasyDotnet.Services;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Credentials;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace EasyDotnet.IDE.Upgrade.Service;

public sealed class ChangelogService(
    NugetService nugetService,
    IHttpClientFactory httpClientFactory,
    ILogger<ChangelogService> logger)
{
  private static readonly string[] TagFormats = [
      "v{0}",
      "{0}",
  ];

  public async Task<ChangelogResult> GetChangelogAsync(string packageId, string version, CancellationToken ct)
  {
    var nugetUrl = $"https://www.nuget.org/packages/{packageId}/{version}";

    if (!NuGetVersion.TryParse(version, out var nugetVersion))
    {
      return new ChangelogResult(packageId, version, null, "none", null, null, nugetUrl);
    }

    var identity = new PackageIdentity(packageId, nugetVersion);

    DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
    using var cache = new SourceCacheContext();

    Uri? projectUrl = null;

    foreach (var source in nugetService.GetSources())
    {
      try
      {
        var sourceRepo = Repository.Factory.GetCoreV3(source.Source);
        var resource = await sourceRepo.GetResourceAsync<PackageMetadataResource>(ct);
        var metadata = await resource.GetMetadataAsync(identity, cache, NullLogger.Instance, ct);

        if (metadata is null) continue;

        projectUrl = metadata.ProjectUrl;
        break;
      }
      catch (Exception ex)
      {
        logger.LogDebug("Failed to fetch metadata from {source}: {ex}", source.Name, ex.Message);
      }
    }

    // Try GitHub releases — most reliable source for OSS packages.
    if (projectUrl is not null && TryParseGitHubRepo(projectUrl, out var owner, out var gitHubRepo))
    {
      var (body, releaseUrl) = await TryFetchGitHubReleaseAsync(owner!, gitHubRepo!, version, ct);
      if (body is not null)
      {
        return new ChangelogResult(packageId, version, body, "github", releaseUrl, projectUrl.ToString(), nugetUrl);
      }
    }

    return new ChangelogResult(packageId, version, null, "none", null, projectUrl?.ToString(), nugetUrl);
  }

  private static bool TryParseGitHubRepo(Uri url, out string? owner, out string? repo)
  {
    owner = null;
    repo = null;
    if (!url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

    var parts = url.AbsolutePath.Trim('/').Split('/');
    if (parts.Length < 2) return false;

    owner = parts[0];
    repo = parts[1];
    return true;
  }

  private async Task<(string? Body, string? Url)> TryFetchGitHubReleaseAsync(
      string owner, string repo, string version, CancellationToken ct)
  {
    try
    {
      var http = httpClientFactory.CreateClient();
      http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "easy-dotnet");

      var releases = await http.GetFromJsonAsync<GitHubRelease[]>(
          $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=20",
          ct);

      if (releases is null) return (null, null);

      foreach (var tagFormat in TagFormats)
      {
        var tag = string.Format(tagFormat, version);
        var match = releases.FirstOrDefault(r =>
            string.Equals(r.TagName, tag, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
          return (match.Body, match.HtmlUrl);
      }
    }
    catch (Exception ex)
    {
      logger.LogDebug("GitHub releases lookup failed for {owner}/{repo}@{ver}: {ex}",
          owner, repo, version, ex.Message);
    }
    return (null, null);
  }

  private sealed class GitHubRelease
  {
    [JsonPropertyName("tag_name")] public string TagName { get; init; } = "";
    [JsonPropertyName("body")] public string? Body { get; init; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
  }
}
