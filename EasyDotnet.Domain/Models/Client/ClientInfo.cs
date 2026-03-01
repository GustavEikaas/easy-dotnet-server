namespace EasyDotnet.Domain.Models.Client;

public sealed record ClientInfo(string Name, string? Version, ClientCapabilities Capabilities);

public sealed record ClientCapabilities(PickerCapabilities Picker);

public sealed record PickerCapabilities(bool SupportsAutoCancel);
