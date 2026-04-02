namespace EasyDotnet.IDE.Models.Client.Prompt;

public sealed record PromptSelectionRequest(string Prompt, SelectionOption[] Choices, string? DefaultSelectionId);
