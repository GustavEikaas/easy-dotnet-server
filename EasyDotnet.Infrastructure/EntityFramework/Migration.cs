namespace EasyDotnet.Infrastructure.EntityFramework;

public sealed record Migration(
    string Id,
    string Name,
    bool Applied,
    string? SafeName = null);
