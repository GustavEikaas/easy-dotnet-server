namespace EasyDotnet.Domain.Models.Client;

public sealed record PromptSelectionRequest(string Prompt, SelectionOption[] Choices, string? DefaultSelectionId);