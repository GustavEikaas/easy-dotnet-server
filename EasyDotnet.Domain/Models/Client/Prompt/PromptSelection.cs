namespace EasyDotnet.Domain.Models.Client;

public sealed record PromptSelectionRequest(Guid Id, string Prompt, SelectionOption[] Choices, string? DefaultSelectionId);