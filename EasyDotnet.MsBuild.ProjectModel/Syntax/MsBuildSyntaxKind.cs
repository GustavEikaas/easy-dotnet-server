namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

public enum MsBuildSyntaxKind
{
  None,

  // Root
  Project,

  // Groups
  PropertyGroup,
  ItemGroup,

  // Properties
  Property,

  // Items
  Item,
  PackageReference,
  ProjectReference,
  Reference,
  Compile,
  Content,
  None_Item,  // Can't use "None" as it conflicts with enum value
  EmbeddedResource,

  // Metadata
  Metadata,

  // Targets
  Target,

  // Import
  Import,

  // Special
  Choose,
  When,
  Otherwise
}