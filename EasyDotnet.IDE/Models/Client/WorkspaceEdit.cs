namespace EasyDotnet.IDE.Models.Client;

public record WorkspaceEdit(DocumentChange[] DocumentChanges);

// Kept with nullable properties to omit errors for container tests and deserialization
// evaluate later with System.Text.Json and polymorphic support when needed 
public record DocumentChange
{
  // CreateFile
  public string? Uri { get; init; }
  public string? Kind { get; init; }

  // TextDocumentEdit
  public TextDocumentIdentifier? TextDocument { get; init; }
  public TextEdit[]? Edits { get; init; }

  public static DocumentChange CreateFile(string uri) => new()
  {
    Kind = "create",
    Uri = uri
  };

  public static DocumentChange TextDocumentEdit(string uri, TextEdit[] edits) => new()
  {
    TextDocument = new TextDocumentIdentifier(uri),
    Edits = edits
  };
}

public record TextDocumentIdentifier(string Uri);

public record TextEdit(TextEditRange Range, string NewText);

public record TextEditRange(TextEditPosition Start, TextEditPosition End);

public record TextEditPosition(int Line, int Character);