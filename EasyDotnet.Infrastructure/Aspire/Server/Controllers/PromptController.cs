using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class PromptController
{
  [JsonRpcMethod("promptForSecretString")]
  public string PromptForSecretString(string token, string promptText, bool required)
  {
    Console.WriteLine($"[{token}] Prompt for secret string: {promptText}, required={required}");
    // return dummy value for testing
    return "secret123";
  }

  [JsonRpcMethod("promptForString")]
  public string PromptForString(string token, string promptText, string? defaultValue, bool required)
  {
    Console.WriteLine($"[{token}] Prompt for string: {promptText}, default={defaultValue}, required={required}");
    // return dummy value for testing
    return defaultValue ?? "userInput";
  }

  [JsonRpcMethod("promptForSelection")]
  public string PromptForSelection(string token, string promptText, string[] choices)
  {
    Console.WriteLine($"[{token}] Prompt for selection: {promptText}");
    Console.WriteLine($"Choices: {string.Join(", ", choices)}");
    // For testing, return the first choice
    return choices.Length > 0 ? choices[0] : string.Empty;
  }

  [JsonRpcMethod("promptForSelections")]
  public string[] PromptForSelections(string token, string promptText, string[] choices)
  {
    Console.WriteLine($"[{token}] Prompt for multiple selections: {promptText}");
    Console.WriteLine($"Choices: {string.Join(", ", choices)}");
    // For testing, return all choices
    return choices;
  }

  [JsonRpcMethod("confirm")]
  public bool Confirm(string token, string promptText, bool defaultValue)
  {
    Console.WriteLine($"[{token}] Confirm prompt: {promptText}, default={defaultValue}");
    // return dummy value for testing
    return defaultValue;
  }
}