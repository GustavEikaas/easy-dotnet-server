namespace EasyDotnet.IDE.Models.Client.Prompt;

public sealed record PromptMultiSelectionRequest(string Prompt, SelectionOption[] Choices);