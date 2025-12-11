namespace EasyDotnet.MsBuild.ProjectModel;

public record ItemTypeInfo(string Name, string Description);

/// <summary>
/// Well-known MSBuild item types used in ItemGroup elements.
/// </summary>
public static class KnownItemTypes
{
  public static IEnumerable<ItemTypeInfo> GetAll() => typeof(KnownItemTypes)
      .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
      .Where(f => f.FieldType == typeof(ItemTypeInfo))
      .Select(f => (ItemTypeInfo)f.GetValue(null)!)
      .Where(item => item is not null);

  public static readonly ItemTypeInfo PackageReference = new(
      Name: "PackageReference",
      Description: "References a NuGet package dependency."
  );

  public static readonly ItemTypeInfo ProjectReference = new(
      Name: "ProjectReference",
      Description: "References another project in the solution."
  );

  public static readonly ItemTypeInfo Reference = new(
      Name: "Reference",
      Description: "References a . NET Framework assembly (legacy)."
  );

  public static readonly ItemTypeInfo Compile = new(
      Name: "Compile",
      Description: "Source files to compile (usually .cs, .vb, .fs)."
  );

  public static readonly ItemTypeInfo Content = new(
      Name: "Content",
      Description: "Files to include in the output directory or publish."
  );

  public static readonly ItemTypeInfo None = new(
      Name: "None",
      Description: "Files tracked by the project but not compiled or copied."
  );

  public static readonly ItemTypeInfo EmbeddedResource = new(
      Name: "EmbeddedResource",
      Description: "Files embedded as resources in the assembly."
  );

  public static readonly ItemTypeInfo AdditionalFiles = new(
      Name: "AdditionalFiles",
      Description: "Files available to source generators and analyzers."
  );

  public static readonly ItemTypeInfo Analyzer = new(
      Name: "Analyzer",
      Description: "Code analyzer assemblies."
  );

  public static readonly ItemTypeInfo InternalsVisibleTo = new(
      Name: "InternalsVisibleTo",
      Description: "Assemblies that can access internal types."
  );

  public static readonly ItemTypeInfo Using = new(
      Name: "Using",
      Description: "Global using directives (C# 10+)."
  );
}