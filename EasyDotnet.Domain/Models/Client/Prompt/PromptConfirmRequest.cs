namespace EasyDotnet.Domain.Models.Client;

public sealed record PromptConfirmRequest(string Prompt, bool DefaultValue);