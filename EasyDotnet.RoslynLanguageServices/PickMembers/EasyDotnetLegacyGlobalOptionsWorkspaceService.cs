using System.Composition;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;

namespace EasyDotnet.RoslynLanguageServices.PickMembers;

[ExportWorkspaceService(typeof(ILegacyGlobalOptionsWorkspaceService)), Shared]
internal sealed class EasyDotnetLegacyGlobalOptionsWorkspaceService : ILegacyGlobalOptionsWorkspaceService
{
  public bool RazorUseTabs => LineFormattingOptions.Default.UseTabs;
  public int RazorTabSize => LineFormattingOptions.Default.TabSize;

  public bool GenerateOverrides { get; set; } = true;

  public bool GetGenerateEqualsAndGetHashCodeFromMembersGenerateOperators(string language) => false;
  public void SetGenerateEqualsAndGetHashCodeFromMembersGenerateOperators(string language, bool value) { }

  public bool GetGenerateEqualsAndGetHashCodeFromMembersImplementIEquatable(string language) => false;
  public void SetGenerateEqualsAndGetHashCodeFromMembersImplementIEquatable(string language, bool value) { }

  public bool GetGenerateConstructorFromMembersOptionsAddNullChecks(string language) => false;
  public void SetGenerateConstructorFromMembersOptionsAddNullChecks(string language, bool value) { }

  public SyntaxFormattingOptions GetSyntaxFormattingOptions(LanguageServices languageServices)
    => SyntaxFormattingOptions.CommonDefaults;
}
