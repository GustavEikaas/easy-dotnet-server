namespace EasyDotnet.Domain.Models.Client;

public sealed record PromptMultiSelectionRequest(string Prompt, SelectionOption[] Choices);