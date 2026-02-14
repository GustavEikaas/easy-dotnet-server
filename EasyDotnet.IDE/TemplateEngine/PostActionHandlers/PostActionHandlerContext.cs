using System.Text.Json;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed record PostActionHandlerContext(
    JsonElement Args,
    IReadOnlyList<ManualInstruction> ManualInstructions,
    bool ContinueOnError);

public sealed record ManualInstruction(string Text);