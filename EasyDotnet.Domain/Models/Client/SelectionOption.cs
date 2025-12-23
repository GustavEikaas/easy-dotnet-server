using Newtonsoft.Json;

namespace EasyDotnet.Domain.Models.Client;

public sealed record SelectionOption<T>(
    string Id,
    string Display,
    [property: JsonIgnore] T? Data = default
);