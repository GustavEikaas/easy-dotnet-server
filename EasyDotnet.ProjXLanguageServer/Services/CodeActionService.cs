using EasyDotnet.ProjXLanguageServer.Services.CodeActions;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface ICodeActionService
{
  CodeAction[] GetCodeActions(CsprojDocument doc, LspRange range, Diagnostic[] contextDiagnostics);
}

public class CodeActionService(IUserSecretsResolver userSecretsResolver) : ICodeActionService
{
  public CodeAction[] GetCodeActions(CsprojDocument doc, LspRange range, Diagnostic[] contextDiagnostics)
  {
    var actions = new List<CodeAction>();

    var startOffset = doc.ToOffset(range.Start.Line, range.Start.Character);
    var endOffset = doc.ToOffset(range.End.Line, range.End.Character);

    AddIfNotNull(actions, SortPackageReferencesAction.Build(doc, startOffset, endOffset));
    AddIfNotNull(actions, ConvertTargetFrameworkAction.BuildToMulti(doc, startOffset, endOffset));
    AddIfNotNull(actions, OpenSecretsAction.Build(doc, startOffset, endOffset, userSecretsResolver));
    AddIfNotNull(actions, ExpandSelfClosingAction.Build(doc, startOffset, endOffset));
    AddIfNotNull(actions, CollapseSelfClosingAction.Build(doc, startOffset, endOffset));

    foreach (var diagnostic in contextDiagnostics)
    {
      AddIfNotNull(actions, RemoveElementAction.BuildForDiagnostic(doc, diagnostic));
      AddIfNotNull(actions, ConvertTargetFrameworkAction.BuildToSingleForDiagnostic(doc, diagnostic));
    }

    return [.. actions];
  }

  private static void AddIfNotNull(List<CodeAction> actions, CodeAction? action)
  {
    if (action != null)
      actions.Add(action);
  }
}