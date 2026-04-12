// ============================================================
// TODO: REMOVE BEFORE MERGE — debug/testing endpoints only
// ============================================================

using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Picker.Models;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerTestController(IEditorService editor) : BaseController
{
    private static readonly PickerChoice<string>[] FruitChoices =
    [
        new("apple",  "Apple",  "apple"),
        new("banana", "Banana", "banana"),
        new("cherry", "Cherry", "cherry"),
        new("date",   "Date",   "date"),
        new("elderberry", "Elderberry", "elderberry"),
    ];

    private static PreviewResult FruitPreview(string fruit, CancellationToken _) => fruit switch
    {
        "apple"      => new PreviewResult.Text(["🍎 Apple", "", "A crisp red fruit.", "Sweet and crunchy."], "markdown"),
        "banana"     => new PreviewResult.Text(["🍌 Banana", "", "A yellow curved fruit.", "Rich in potassium."], "markdown"),
        "cherry"     => new PreviewResult.Text(["🍒 Cherry", "", "A small red stone fruit.", "Often used in desserts."], "markdown"),
        "date"       => new PreviewResult.Text(["🌴 Date", "", "A sweet dried fruit.", "Staple of Middle Eastern cuisine."], "markdown"),
        "elderberry" => new PreviewResult.Text(["🫐 Elderberry", "", "A dark berry.", "Used in syrups and wines."], "markdown"),
        _            => new PreviewResult.Text(["Unknown fruit"], null),
    };

    [JsonRpcMethod("_test/picker")]
    public async Task TestPicker(CancellationToken ct)
    {
        var selected = await editor.RequestPickerAsync("Pick a fruit", FruitChoices, ct: ct);
        var msg = selected is null ? "Cancelled" : $"Selected: {selected}";
        await editor.DisplayMessage($"[TestPicker] {msg}");
    }

    [JsonRpcMethod("_test/picker-preview")]
    public async Task TestPickerPreview(CancellationToken ct)
    {
        var selected = await editor.RequestPickerAsync(
            "Pick a fruit (with preview)",
            FruitChoices,
            (fruit, token) => Task.FromResult(FruitPreview(fruit, token)),
            ct);

        var msg = selected is null ? "Cancelled" : $"Selected: {selected}";
        await editor.DisplayMessage($"[TestPickerPreview] {msg}");
    }

    [JsonRpcMethod("_test/multi-picker-preview")]
    public async Task TestMultiPickerPreview(CancellationToken ct)
    {
        var selected = await editor.RequestMultiPickerAsync(
            "Pick fruits (multi, with preview)",
            FruitChoices,
            (fruit, token) => Task.FromResult(FruitPreview(fruit, token)),
            ct);

        var msg = selected is null
            ? "Cancelled"
            : selected.Length == 0
                ? "None selected"
                : $"Selected: {string.Join(", ", selected)}";

        await editor.DisplayMessage($"[TestMultiPickerPreview] {msg}");
    }

    [JsonRpcMethod("_test/live-preview")]
    public async Task TestLivePreview(CancellationToken ct)
    {
        // Simulates a live picker: query filters fruit names, preview shows details.
        var selected = await editor.RequestLivePickerAsync<string>(
            "Live fruit search",
            (query, token) =>
            {
                var results = FruitChoices
                    .Where(c => c.Display.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return Task.FromResult(results);
            },
            (fruit, token) => Task.FromResult(FruitPreview(fruit, token)),
            ct);

        var msg = selected is null ? "Cancelled" : $"Selected: {selected}";
        await editor.DisplayMessage($"[TestLivePreview] {msg}");
    }
}
