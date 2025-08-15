using System.Collections.Generic;

namespace EasyDotnet.Controllers.Template;

public sealed record DotnetNewParameterResponse(string Name, string? DefaultValue, string? DefaultIfOptionWithoutValue, string DataType, string? Description, bool IsRequired, IReadOnlyDictionary<string, string>? Choices);