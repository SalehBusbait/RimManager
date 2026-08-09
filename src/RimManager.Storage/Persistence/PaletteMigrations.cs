using System.Text.Json.Nodes;
using RimManager.Core.Domain;

namespace RimManager.Storage.Persistence;

/// <summary>
/// Schema-v1 → v2 migrations that move tag and separator colours off hex strings
/// and onto a <see cref="Palette"/> index (design non-negotiable #6).
/// <para>
/// A stored <c>#4FBF87</c> is the <em>dark</em> green; rendered on the light theme it
/// is illegible, and the fault is in the data rather than the UI. Existing files
/// therefore have to be rewritten once. <see cref="JsonDocumentStore{T}"/> applies
/// these to the raw <c>data</c> node before deserialization, and it has already
/// written a timestamped backup, so the step is reversible.
/// </para>
/// <para>
/// Both migrations are deliberately tolerant: an unreadable or absent colour maps
/// to <see cref="Palette.Blue"/> rather than throwing. Losing a user's whole tag
/// list because one colour was hand-edited to <c>"puce"</c> would be a far worse
/// outcome than one tag coming back the wrong shade.
/// </para>
/// </summary>
public static class PaletteMigrations
{
    /// <summary><c>tags.json</c> v1 → v2: <c>color: "#4FBF87"</c> → <c>paletteIndex: 1</c>.</summary>
    public static JsonObject TagsV1ToV2(JsonObject data)
    {
        if (data["tags"] is not JsonArray tags) return data;

        foreach (var node in tags)
        {
            if (node is not JsonObject tag) continue;
            ConvertColour(tag);
        }

        return data;
    }

    /// <summary>
    /// Profile and snapshot documents v1 → v2: the same conversion on every
    /// separator entry inside <c>state.entries</c>.

    private static void MigrateEntries(JsonObject state)
    {
        if (state["entries"] is not JsonArray entries) return;

        foreach (var node in entries)
        {
            if (node is not JsonObject entry) continue;

            // Mod entries never carried a colour; only separators need touching.
            var isSeparator = entry["kind"]?.GetValue<string>() is { } kind
                && kind.Equals("Separator", StringComparison.OrdinalIgnoreCase);
            if (!isSeparator) continue;

            ConvertColour(entry);
        }
    }

    /// <summary>
    /// Replaces a <c>color</c> property with <c>paletteIndex</c>. A separator with no
    /// colour keeps none — null means "inherit", not "blue".
    /// </summary>
    private static void ConvertColour(JsonObject target)
    {
        if (!target.ContainsKey("color")) return;

        var hex = target["color"]?.GetValue<string>();
        target.Remove("color");

        if (string.IsNullOrWhiteSpace(hex)) return;
        target["paletteIndex"] = Palette.NearestTo(hex);
    }
}
