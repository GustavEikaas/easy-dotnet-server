using System;
using System.Collections.Generic;
using NuGet.Protocol.Core.Types;

namespace EasyDotnet.Controllers.Nuget;

public sealed record NugetPackageMetadata(
    string Source,
    string Id,
    string Version,
    string? Authors,
    string? Description,
    long? DownloadCount,
    Uri? LicenseUrl,
    IReadOnlyList<string> Owners,
    Uri? ProjectUrl,
    Uri? ReadmeUrl,
    string? Summary,
    IReadOnlyList<string> Tags,
    string? Title,
    bool PrefixReserved,
    bool IsListed)
{
  public static NugetPackageMetadata From(IPackageSearchMetadata metadata, string source) => new(
      Source: source,
      Id: metadata.Identity.Id,
      Version: metadata.Identity.Version.ToString(),
      Authors: metadata.Authors,
      Description: metadata.Description,
      DownloadCount: metadata.DownloadCount,
      LicenseUrl: metadata.LicenseUrl,
      Owners: metadata.OwnersList,
      ProjectUrl: metadata.ProjectUrl,
      ReadmeUrl: metadata.ReadmeUrl,
      Summary: metadata.Summary,
      Tags: metadata.Tags?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
      Title: metadata.Title,
      PrefixReserved: metadata.PrefixReserved,
      IsListed: metadata.IsListed
  );
}