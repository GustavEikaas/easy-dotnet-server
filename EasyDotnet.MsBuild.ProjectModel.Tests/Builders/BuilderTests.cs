using EasyDotnet.MsBuild.ProjectModel.Builders;
using EasyDotnet.MsBuild.ProjectModel.Extensions;
using EasyDotnet.MsBuild.ProjectModel.Syntax;

namespace EasyDotnet.MsBuild.ProjectModel.Tests.Builders;

public class BuilderTests
{
  [Test]
  public async Task ProjectBuilder_BuildsSimpleProject()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddPropertyGroup(pg => pg
            .AddProperty(MsBuildProperties.TargetFramework.Name, "net8.0")
            .AddProperty(MsBuildProperties.Nullable.Name, "enable"))
        .Build();

    await Assert.That(project.Sdk).IsEqualTo(KnownSdks.MicrosoftNetSdk.Name);
    await Assert.That(project.PropertyGroups).HasCount().EqualTo(1);
    await Assert.That(project.PropertyGroups[0].Properties[0].Value).IsEqualTo("net8.0");
    await Assert.That(project.PropertyGroups[0].Properties[1].Value).IsEqualTo("enable");
  }

  [Test]
  public async Task ProjectBuilder_ToXml_GeneratesValidXml()
  {
    var builder = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddPropertyGroup(pg => pg
            .AddProperty(MsBuildProperties.TargetFramework.Name, "net8.0"));

    const string expected = """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
      </PropertyGroup>
    </Project>
    
    """;

    var xml = builder.ToXml();

    await Assert.That(xml).IsEqualTo(expected);
  }

  [Test]
  public async Task ProjectBuilder_WithPropertyGroupCondition()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddPropertyGroup(pg => pg
            .WithCondition("'$(Configuration)' == 'Debug'")
            .AddProperty(MsBuildProperties.DebugSymbols.Name, "true"))
        .Build();

    await Assert.That(project.PropertyGroups[0].Condition).IsEqualTo("'$(Configuration)' == 'Debug'");
  }

  [Test]
  public async Task ProjectBuilder_WithMultiplePropertyGroups()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddPropertyGroup(pg => pg
            .AddProperty(MsBuildProperties.TargetFramework.Name, "net8.0"))
        .AddPropertyGroup(pg => pg
            .WithCondition("'$(Configuration)' == 'Release'")
            .AddProperty(MsBuildProperties.Optimize.Name, "true"))
        .Build();

    await Assert.That(project.PropertyGroups).HasCount().EqualTo(2);
    await Assert.That(project.PropertyGroups[1].Condition).IsEqualTo("'$(Configuration)' == 'Release'");
  }

  [Test]
  public async Task ProjectBuilder_WithItemGroup_AddPackageReference()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddItemGroup(ig => ig
            .AddPackageReference("Newtonsoft.Json", "13.0.1"))
        .Build();

    await Assert.That(project.ItemGroups).HasCount().EqualTo(1);
    await Assert.That(project.ItemGroups[0].Items).HasCount().EqualTo(1);

    var item = project.ItemGroups[0].Items[0];
    await Assert.That(item.ItemType).IsEqualTo(MsBuildSyntaxKind.PackageReference.ToElementName());
    await Assert.That(item.Include).IsEqualTo("Newtonsoft.Json");
    await Assert.That(item.GetMetadataValue("Version")).IsEqualTo("13.0.1");
  }

  [Test]
  public async Task ProjectBuilder_RoundTrip_ParseAndRegenerate()
  {
    const string originalXml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;

    var tree = MsBuildSyntaxTree.Parse(originalXml);
    var regeneratedXml = tree.Root.ToXml();
    var reparsedTree = MsBuildSyntaxTree.Parse(regeneratedXml);

    await Assert.That(reparsedTree.Root.Sdk).IsEqualTo(tree.Root.Sdk);
    await Assert.That(reparsedTree.Root.PropertyGroups).HasCount().EqualTo(tree.Root.PropertyGroups.Length);
  }

  [Test]
  public async Task ProjectBuilder_ComplexProject()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddPropertyGroup(pg => pg
            .AddProperties(
                (MsBuildProperties.TargetFramework.Name, "net8.0"),
                (MsBuildProperties.OutputType.Name, "Exe"),
                (MsBuildProperties.Nullable.Name, "enable")))
        .AddItemGroup(ig => ig
            .AddPackageReference("Newtonsoft.Json", "13.0.1")
            .AddPackageReference("Serilog", "3.0.0"))
        .AddItemGroup(ig => ig
            .AddProjectReference("../Core/Core.csproj"))
        .Build();

    await Assert.That(project.PropertyGroups).HasCount().EqualTo(1);
    await Assert.That(project.ItemGroups).HasCount().EqualTo(2);
    await Assert.That(project.ItemGroups[0].Items).HasCount().EqualTo(2);
    await Assert.That(project.ItemGroups[1].Items).HasCount().EqualTo(1);
  }

  [Test]
  public async Task ProjectBuilder_WithWebSdk()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdkWeb.Name)
        .AddPropertyGroup(pg => pg
            .AddProperty(MsBuildProperties.TargetFramework.Name, "net8.0"))
        .AddItemGroup(ig => ig
            .AddPackageReference("Swashbuckle.AspNetCore", "6.5.0"))
        .Build();

    await Assert.That(project.Sdk).IsEqualTo("Microsoft.NET.Sdk.Web");
    await Assert.That(project.ItemGroups[0].Items[0].Include).IsEqualTo("Swashbuckle.AspNetCore");
  }

  [Test]
  public async Task ProjectBuilder_WithMultipleItemTypes()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddItemGroup(ig => ig
            .AddPackageReference("xunit", "2.6.0")
            .AddProjectReference("../Shared/Shared.csproj")
            .AddCompile("Generated/Code.cs")
            .AddContent("appsettings.json", "PreserveNewest"))
        .Build();

    var items = project.ItemGroups[0].Items;
    await Assert.That(items).HasCount().EqualTo(4);
    await Assert.That(items[0].ItemType).IsEqualTo(MsBuildSyntaxKind.PackageReference.ToElementName());
    await Assert.That(items[1].ItemType).IsEqualTo(MsBuildSyntaxKind.ProjectReference.ToElementName());
    await Assert.That(items[2].ItemType).IsEqualTo(MsBuildSyntaxKind.Compile.ToElementName());
    await Assert.That(items[3].ItemType).IsEqualTo(MsBuildSyntaxKind.Content.ToElementName());
  }

  [Test]
  public async Task ProjectBuilder_TestProject_WithTestingFramework()
  {
    var project = new ProjectBuilder()
        .WithSdk(KnownSdks.MicrosoftNetSdk.Name)
        .AddPropertyGroup(pg => pg
            .AddProperty(MsBuildProperties.IsTestProject.Name, "true")
            .AddProperty(MsBuildProperties.TargetFramework.Name, "net8.0"))
        .AddItemGroup(ig => ig
            .AddPackageReference("xunit", "2.6.0")
            .AddPackageReference("xunit.runner.visualstudio", "2.5.0"))
        .Build();

    await Assert.That(project.PropertyGroups[0].Properties).HasCount().EqualTo(2);
    await Assert.That(project.ItemGroups[0].Items).HasCount().EqualTo(2);
  }

  [Test]
  public async Task MsBuildSyntaxKind_ToElementName_ReturnsCorrectName()
  {
    await Assert.That(MsBuildSyntaxKind.PackageReference.ToElementName()).IsEqualTo("PackageReference");
    await Assert.That(MsBuildSyntaxKind.ProjectReference.ToElementName()).IsEqualTo("ProjectReference");
    await Assert.That(MsBuildSyntaxKind.PropertyGroup.ToElementName()).IsEqualTo("PropertyGroup");
    await Assert.That(MsBuildSyntaxKind.ItemGroup.ToElementName()).IsEqualTo("ItemGroup");
  }

  [Test]
  public async Task MsBuildSyntaxKind_IsItemType_IdentifiesItemTypesCorrectly()
  {
    await Assert.That(MsBuildSyntaxKind.PackageReference.IsItemType()).IsTrue();
    await Assert.That(MsBuildSyntaxKind.Compile.IsItemType()).IsTrue();
    await Assert.That(MsBuildSyntaxKind.Content.IsItemType()).IsTrue();
    await Assert.That(MsBuildSyntaxKind.PropertyGroup.IsItemType()).IsFalse();
    await Assert.That(MsBuildSyntaxKind.Target.IsItemType()).IsFalse();
  }

  [Test]
  public async Task MsBuildSyntaxKind_GetDescription_ReturnsDescription()
  {
    var description = MsBuildSyntaxKind.PackageReference.GetDescription();
    await Assert.That(description).IsEqualTo("NuGet package reference");
  }

  [Test]
  public async Task KnownSdks_GetAll_ReturnsAllSdks()
  {
    var allSdks = KnownSdks.GetAll().ToList();

    await Assert.That(allSdks).HasCount().GreaterThanOrEqualTo(5);
    await Assert.That(allSdks.Any(s => s.Name == "Microsoft.NET.Sdk")).IsTrue();
    await Assert.That(allSdks.Any(s => s.Name == "Microsoft.NET.Sdk.Web")).IsTrue();
  }

  [Test]
  public async Task MsBuildSyntaxKind_GetAllItemTypes_ReturnsAllItemTypes()
  {
    var itemTypes = MsBuildSyntaxKindExtensions.GetAllItemTypes().ToList();

    await Assert.That(itemTypes).HasCount().GreaterThanOrEqualTo(7);
    await Assert.That(itemTypes).Contains(MsBuildSyntaxKind.PackageReference);
    await Assert.That(itemTypes).Contains(MsBuildSyntaxKind.Compile);
    await Assert.That(itemTypes).Contains(MsBuildSyntaxKind.Content);
  }

  [Test]
  public async Task ProjectMutations_AddPackageReferenceToExistingItemGroup()
  {
    const string originalXml = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net8.0</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Newtonsoft.Json">
            <Version>13.0.1</Version>
          </PackageReference>
          <PackageReference Include="Serilog">
            <Version>3.0.0</Version>
          </PackageReference>
        </ItemGroup>
      </Project>
      """;

    var project = MsBuildSyntaxTree.Parse(originalXml).Root;
    var packageItemGroup = project.ItemGroups
        .First(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.PackageReference));

    var newPackage = new ItemBuilder()
        .WithItemType(MsBuildSyntaxKind.PackageReference.ToElementName())
        .WithInclude("xunit")
        .WithAttribute("Version", "2.6.0")
        .BuildDraft();

    var dirtyTree = packageItemGroup.WithItem(newPackage);
    var updatedXml = dirtyTree.ToXml();

    const string expectedXml = """
      <ItemGroup>
        <PackageReference Include="Newtonsoft.Json">
          <Version>13.0.1</Version>
        </PackageReference>
        <PackageReference Include="Serilog">
          <Version>3.0.0</Version>
        </PackageReference>
        <PackageReference Include="xunit" Version="2.6.0" />
      </ItemGroup>
      
      """;

    await Assert.That(updatedXml).IsEqualTo(expectedXml);

    var rebuilt = dirtyTree.Rebuild();
    await Assert.That(rebuilt.ItemGroups[0].Items).HasCount().EqualTo(3);
    await Assert.That(rebuilt.ItemGroups[0].Items[2].Include).IsEqualTo("xunit");
    await Assert.That(rebuilt.ItemGroups[0].Items[2].Span.Start).IsGreaterThan(0);
  }
}