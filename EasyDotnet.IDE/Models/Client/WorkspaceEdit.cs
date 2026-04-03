namespace EasyDotnet.IDE.Models.Client;

public record WorkspaceEdit(WorkspaceDocumentChange[] DocumentChanges);

public record WorkspaceDocumentChange(TextDocumentIdentifier TextDocument, TextEdit[] Edits);

public record TextDocumentIdentifier(string Uri);

public record TextEdit(TextEditRange Range, string NewText);

public record TextEditRange(TextEditPosition Start, TextEditPosition End);

public record TextEditPosition(int Line, int Character);
