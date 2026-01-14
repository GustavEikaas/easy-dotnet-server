namespace EasyDotnet.Domain.Models.Client;

public sealed record ClientInfo(string Name, string? Version);

public sealed record ClientCapabilities(PickerCapabilities PickerCapabilities);

public sealed record PickerCapabilities(bool SupportsAutoCancel);
