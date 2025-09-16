namespace EasyDotnet.Services.NetCoreDbg;

public record DapVariablesResponse(
    string Name,
    string Value,
    string? Type,
    int VariablesReference
);