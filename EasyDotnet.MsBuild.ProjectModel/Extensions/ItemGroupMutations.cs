using EasyDotnet.MsBuild.ProjectModel.Builders;
using EasyDotnet.MsBuild.ProjectModel.Syntax;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Extensions;

/// <summary>
/// High-level mutations for common ItemGroup operations.
/// </summary>
public static class ItemGroupMutations
{
  /// <summary>
  /// Adds a PackageReference to this ItemGroup.
  /// </summary>
  public static DirtyProjectSyntax AddPackageReference(
      this ItemGroupSyntax itemGroup,
      string packageName,
      string? version = null)
  {
    var builder = new ItemBuilder()
        .WithItemType(MsBuildSyntaxKind.PackageReference.ToElementName())
        .WithInclude(packageName);

    if (version != null)
    {
      builder.WithAttribute("Version", version);
    }

    return itemGroup.WithItem(builder.BuildDraft());
  }

  /// <summary>
  /// Adds a ProjectReference to this ItemGroup.
  /// </summary>
  public static DirtyProjectSyntax AddProjectReference(
      this ItemGroupSyntax itemGroup,
      string projectPath)
  {
    var builder = new ItemBuilder()
        .WithItemType(MsBuildSyntaxKind.ProjectReference.ToElementName())
        .WithInclude(projectPath);

    return itemGroup.WithItem(builder.BuildDraft());
  }

  /// <summary>
  /// Adds a Compile item to this ItemGroup.
  /// </summary>
  public static DirtyProjectSyntax AddCompile(
      this ItemGroupSyntax itemGroup,
      string filePath)
  {
    var builder = new ItemBuilder()
        .WithItemType(MsBuildSyntaxKind.Compile.ToElementName())
        .WithInclude(filePath);

    return itemGroup.WithItem(builder.BuildDraft());
  }

  /// <summary>
  /// Adds a Content item to this ItemGroup.
  /// </summary>
  public static DirtyProjectSyntax AddContent(
      this ItemGroupSyntax itemGroup,
      string filePath,
      string? copyToOutputDirectory = null)
  {
    var builder = new ItemBuilder()
        .WithItemType(MsBuildSyntaxKind.Content.ToElementName())
        .WithInclude(filePath);

    if (copyToOutputDirectory != null)
    {
      builder.AddMetadata("CopyToOutputDirectory", copyToOutputDirectory);
    }

    return itemGroup.WithItem(builder.BuildDraft());
  }
}