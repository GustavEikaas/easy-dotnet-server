using System.Collections.Immutable;
using EasyDotnet.MsBuild.ProjectModel.Builders;
using EasyDotnet.MsBuild.ProjectModel.Syntax;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Extensions;

public static class ProjectMutations
{
  /// <summary>
  /// Adds an item to an ItemGroup.  Returns a dirty tree that needs rebuilding.
  /// </summary>
  public static DirtyProjectSyntax WithItem(
      this ItemGroupSyntax itemGroup,
      ItemSyntaxDraft itemDraft)
  {
    // Create draft version of ItemGroup with added item
    var draftItems = itemGroup.Items
        .Select(i => i.ToDraft())
        .Append(itemDraft)
        .ToImmutableArray();

    var updatedDraft = new ItemGroupSyntaxDraft
    {
      Condition = itemGroup.Condition,
      Items = draftItems
    };

    return new DirtyProjectSyntax(itemGroup, updatedDraft);
  }

  /// <summary>
  /// Adds an ItemGroup to the Project.
  /// </summary>
  public static DirtyProjectSyntax WithItemGroup(
      this ProjectSyntax project,
      ItemGroupSyntaxDraft itemGroupDraft)
  {
    var projectDraft = project.ToDraft();

    var updatedDraft = projectDraft with
    {
      ItemGroups = projectDraft.ItemGroups.Add(itemGroupDraft)
    };

    return new DirtyProjectSyntax(project, updatedDraft);
  }

  /// <summary>
  /// Replaces an existing ItemGroup in the Project.
  /// </summary>
  public static DirtyProjectSyntax WithReplacedItemGroup(
      this ProjectSyntax project,
      ItemGroupSyntax oldItemGroup,
      ItemGroupSyntaxDraft newItemGroupDraft)
  {
    var projectDraft = project.ToDraft();

    var index = Array.IndexOf([.. project.ItemGroups], oldItemGroup);
    if (index == -1)
      throw new ArgumentException("ItemGroup not found in project", nameof(oldItemGroup));

    var updatedItemGroups = projectDraft.ItemGroups.ToBuilder();
    updatedItemGroups[index] = newItemGroupDraft;

    var updatedDraft = projectDraft with
    {
      ItemGroups = updatedItemGroups.ToImmutable()
    };

    return new DirtyProjectSyntax(project, updatedDraft);
  }

  /// <summary>
  /// Adds a property to a PropertyGroup. Returns a dirty tree that needs rebuilding.
  /// </summary>
  public static DirtyProjectSyntax WithProperty(
      this PropertyGroupSyntax propertyGroup,
      PropertySyntaxDraft propertyDraft)
  {
    var draftProperties = propertyGroup.Properties
        .Select(p => p.ToDraft())
        .Append(propertyDraft)
        .ToImmutableArray();

    var updatedDraft = new PropertyGroupSyntaxDraft
    {
      Condition = propertyGroup.Condition,
      Properties = draftProperties
    };

    return new DirtyProjectSyntax(propertyGroup, updatedDraft);
  }

  /// <summary>
  /// Converts a materialized ItemSyntax back to draft for mutations.
  /// </summary>
  public static ItemSyntaxDraft ToDraft(this ItemSyntax item) => new()
  {
    ItemType = item.ItemType,
    Include = item.Include,
    Exclude = item.Exclude,
    Remove = item.Remove,
    Update = item.Update,
    Condition = item.Condition,
    Metadata = [.. item.Metadata.Select(m => m.ToDraft())]
  };

  /// <summary>
  /// Converts a materialized ProjectSyntax back to draft for mutations.
  /// </summary>
  public static ProjectSyntaxDraft ToDraft(this ProjectSyntax project) => new()
  {
    Sdk = project.Sdk,
    PropertyGroups = [.. project.PropertyGroups.Select(pg => pg.ToDraft())],
    ItemGroups = [.. project.ItemGroups.Select(ig => ig.ToDraft())]
  };

  /// <summary>
  /// Converts a materialized MetadataSyntax back to draft for mutations.
  /// </summary>
  public static MetadataSyntaxDraft ToDraft(this MetadataSyntax metadata) => new()
  {
    Name = metadata.Name,
    Value = metadata.Value
  };

  /// <summary>
  /// Converts a materialized PropertySyntax back to draft for mutations.
  /// </summary>
  public static PropertySyntaxDraft ToDraft(this PropertySyntax property) => new()
  {
    Name = property.Name,
    Value = property.Value,
    Condition = property.Condition
  };

  /// <summary>
  /// Converts a materialized ItemGroupSyntax back to draft for mutations.
  /// </summary>
  public static ItemGroupSyntaxDraft ToDraft(this ItemGroupSyntax itemGroup) => new()
  {
    Condition = itemGroup.Condition,
    Items = [.. itemGroup.Items.Select(i => i.ToDraft())]
  };

  /// <summary>
  /// Converts a materialized PropertyGroupSyntax back to draft for mutations.
  /// </summary>
  public static PropertyGroupSyntaxDraft ToDraft(this PropertyGroupSyntax propertyGroup) => new()
  {
    Condition = propertyGroup.Condition,
    Properties = [.. propertyGroup.Properties.Select(p => p.ToDraft())]
  };

  /// <summary>
  /// Adds a PackageReference to the first ItemGroup that contains PackageReferences,
  /// or creates a new ItemGroup if none exists.
  /// </summary>
  public static DirtyProjectSyntax AddPackageReference(
      this ProjectSyntax project,
      string packageName,
      string? version = null)
  {
    var packageGroup = project.ItemGroups
        .FirstOrDefault(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.PackageReference) && ig.Condition is null);

    if (packageGroup != null)
    {
      // Modify existing ItemGroup
      var itemBuilder = new ItemBuilder()
          .WithItemType(MsBuildSyntaxKind.PackageReference.ToElementName())
          .WithInclude(packageName);

      if (version != null)
        itemBuilder.WithAttribute("Version", version);

      var newItem = itemBuilder.BuildDraft();
      var updatedGroupDraft = packageGroup.ToDraft() with
      {
        Items = packageGroup.ToDraft().Items.Add(newItem)
      };

      return project.WithReplacedItemGroup(packageGroup, updatedGroupDraft);
    }
    else
    {
      // Create new ItemGroup
      var itemBuilder = new ItemBuilder()
          .WithItemType(MsBuildSyntaxKind.PackageReference.ToElementName())
          .WithInclude(packageName);

      if (version != null)
        itemBuilder.WithAttribute("Version", version);

      var newGroupDraft = new ItemGroupSyntaxDraft
      {
        Items = [itemBuilder.BuildDraft()]
      };

      return project.WithItemGroup(newGroupDraft);
    }
  }

  /// <summary>
  /// Adds a ProjectReference to the first ItemGroup that contains ProjectReferences,
  /// or creates a new ItemGroup if none exists.
  /// </summary>
  public static DirtyProjectSyntax AddProjectReference(
      this ProjectSyntax project,
      string projectPath)
  {
    var projectRefGroup = project.ItemGroups
        .FirstOrDefault(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.ProjectReference) && ig.Condition is null);

    if (projectRefGroup != null)
    {
      var newItem = new ItemBuilder()
          .WithItemType(MsBuildSyntaxKind.ProjectReference.ToElementName())
          .WithInclude(projectPath)
          .BuildDraft();

      var updatedGroupDraft = projectRefGroup.ToDraft() with
      {
        Items = projectRefGroup.ToDraft().Items.Add(newItem)
      };

      return project.WithReplacedItemGroup(projectRefGroup, updatedGroupDraft);
    }
    else
    {
      var newGroupDraft = new ItemGroupSyntaxDraft
      {
        Items = [new ItemBuilder()
                .WithItemType(MsBuildSyntaxKind. ProjectReference.ToElementName())
                .WithInclude(projectPath)
                .BuildDraft()]
      };

      return project.WithItemGroup(newGroupDraft);
    }
  }
}