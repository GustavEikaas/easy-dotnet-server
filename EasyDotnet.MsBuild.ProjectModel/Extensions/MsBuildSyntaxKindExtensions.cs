using EasyDotnet.MsBuild.ProjectModel.Syntax;

namespace EasyDotnet.MsBuild.ProjectModel.Extensions;

public static class MsBuildSyntaxKindExtensions
{
  /// <summary>
  /// Gets the XML element name for this syntax kind.
  /// </summary>
  public static string ToElementName(this MsBuildSyntaxKind kind) => kind switch
  {
    MsBuildSyntaxKind.Project => "Project",
    MsBuildSyntaxKind.PropertyGroup => "PropertyGroup",
    MsBuildSyntaxKind.ItemGroup => "ItemGroup",
    MsBuildSyntaxKind.Property => "Property",
    MsBuildSyntaxKind.Item => "Item",
    MsBuildSyntaxKind.PackageReference => "PackageReference",
    MsBuildSyntaxKind.ProjectReference => "ProjectReference",
    MsBuildSyntaxKind.Reference => "Reference",
    MsBuildSyntaxKind.Compile => "Compile",
    MsBuildSyntaxKind.Content => "Content",
    MsBuildSyntaxKind.None_Item => "None",
    MsBuildSyntaxKind.EmbeddedResource => "EmbeddedResource",
    MsBuildSyntaxKind.Metadata => "Metadata",
    MsBuildSyntaxKind.Target => "Target",
    MsBuildSyntaxKind.Import => "Import",
    MsBuildSyntaxKind.Choose => "Choose",
    MsBuildSyntaxKind.When => "When",
    MsBuildSyntaxKind.Otherwise => "Otherwise",
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown syntax kind")
  };

  /// <summary>
  /// Gets a human-readable description of this syntax kind.
  /// </summary>
  public static string GetDescription(this MsBuildSyntaxKind kind) => kind switch
  {
    MsBuildSyntaxKind.Project => "Root project element",
    MsBuildSyntaxKind.PropertyGroup => "Container for MSBuild properties",
    MsBuildSyntaxKind.ItemGroup => "Container for MSBuild items",
    MsBuildSyntaxKind.Property => "MSBuild property definition",
    MsBuildSyntaxKind.Item => "Generic MSBuild item",
    MsBuildSyntaxKind.PackageReference => "NuGet package reference",
    MsBuildSyntaxKind.ProjectReference => "Reference to another project",
    MsBuildSyntaxKind.Reference => "Assembly reference (legacy)",
    MsBuildSyntaxKind.Compile => "Source file to compile",
    MsBuildSyntaxKind.Content => "Content file to include in output",
    MsBuildSyntaxKind.None_Item => "File tracked by project but not compiled",
    MsBuildSyntaxKind.EmbeddedResource => "Resource embedded in assembly",
    MsBuildSyntaxKind.Metadata => "Item metadata",
    MsBuildSyntaxKind.Target => "MSBuild target definition",
    MsBuildSyntaxKind.Import => "Import external MSBuild file",
    MsBuildSyntaxKind.Choose => "Conditional branching container",
    MsBuildSyntaxKind.When => "Conditional branch",
    MsBuildSyntaxKind.Otherwise => "Default branch in Choose",
    _ => "Unknown element type"
  };

  /// <summary>
  /// Determines if this syntax kind represents an item type.
  /// </summary>
  public static bool IsItemType(this MsBuildSyntaxKind kind) => kind switch
  {
    MsBuildSyntaxKind.Item or
    MsBuildSyntaxKind.PackageReference or
    MsBuildSyntaxKind.ProjectReference or
    MsBuildSyntaxKind.Reference or
    MsBuildSyntaxKind.Compile or
    MsBuildSyntaxKind.Content or
    MsBuildSyntaxKind.None_Item or
    MsBuildSyntaxKind.EmbeddedResource => true,
    _ => false
  };

  /// <summary>
  /// Gets all item type syntax kinds.
  /// </summary>
  public static IEnumerable<MsBuildSyntaxKind> GetAllItemTypes()
  {
    yield return MsBuildSyntaxKind.PackageReference;
    yield return MsBuildSyntaxKind.ProjectReference;
    yield return MsBuildSyntaxKind.Reference;
    yield return MsBuildSyntaxKind.Compile;
    yield return MsBuildSyntaxKind.Content;
    yield return MsBuildSyntaxKind.None_Item;
    yield return MsBuildSyntaxKind.EmbeddedResource;
  }
}