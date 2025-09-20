namespace EasyDotnet.Types;

public sealed record DiscoveredTest
{
  public required string Id { get; set; }
  public required string? Namespace { get; set; }
  public required string Name { get; set; }
  public required string DisplayName { get; set; }
  public required string? FilePath { get; set; }
  public required int? LineNumber { get; set; }
}