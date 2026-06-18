namespace EasyDotnet.IDE.Controllers.Template;

public sealed record DotnetNewTemplateResponse(string DisplayName, string Name, string Identity, string? Type, bool IsNameRequired, IReadOnlyList<string> ShortNames);