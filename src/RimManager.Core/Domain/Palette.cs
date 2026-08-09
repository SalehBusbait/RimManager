using System.Collections.Immutable;
using System.Globalization;

namespace RimManager.Core.Domain;

/// <summary>
/// The six-hue tag and separator palette, addressed <em>by index</em>.
/// <para>
/// Design non-negotiable #6: "Store per tag/separator as a palette index, never a
/// hex value — so user colours flip correctly with the theme. This is the single
/// most important data-model consequence of proper theming: never persist a
/// colour, persist a token name."
/// </para>
/// <para>
/// A stored <c>#4FBF87</c> is the *dark* green. Rendered on the light theme it is
/// illegible, and no amount of UI work fixes it — the fault is in the data. An
/// index resolves through the theme dictionary at paint time, so the same tag is
/// legible in both.
/// </para>
/// </summary>
public static class Palette
{
    public const int Count = 6;

    public const int Blue = 0;
    public const int Green = 1;
    public const int Amber = 2;
    public const int Red = 3;
    public const int Violet = 4;
    public const int Slate = 5;

    /// <summary>Hue names in index order. These are role names, not brightness names.</summary>
    public static readonly ImmutableArray<string> Names =
        ["Blue", "Green", "Amber", "Red", "Violet", "Slate"];

    /// <summary>
    /// Reference RGB per hue, used <em>only</em> to migrate legacy stored hex values
    /// onto an index. Rendering never reads these — it resolves
    /// <c>RmPalette{n}Brush</c> from the active theme dictionary, which is the whole
    /// point of storing an index. The dark column is used as the reference because
    /// that is where the legacy values came from.
    /// </summary>
    private static readonly (byte R, byte G, byte B)[] MigrationReference =
    [
        (0x5B, 0x9D, 0xF9), // blue
        (0x4F, 0xBF, 0x87), // green
        (0xE3, 0xB3, 0x41), // amber
        (0xF0, 0x71, 0x6A), // red
        (0xC7, 0x7D, 0xDF), // violet
        (0x7A, 0x82, 0x8E), // slate
    ];

    /// <summary>Wraps any int into a valid index, so "next colour" needs no bounds check.</summary>
    public static int Normalize(int index) => ((index % Count) + Count) % Count;

    /// <summary>
    /// An advisory hex for a hue, for <em>interchange only</em> — a <c>.rwlist</c> is
    /// read by other people's tools, and a bare index means nothing to them. Never
    /// use this to paint: rendering resolves <c>RmPalette{n}Brush</c> from the active
    /// theme, which is the entire reason the index exists.
    /// </summary>
    public static string ReferenceHex(int index)
    {
        var (r, g, b) = MigrationReference[Normalize(index)];
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>The next hue, wrapping — what the separator ⋮ "Color ▸" cycle uses.</summary>
    public static int Next(int index) => Normalize(index + 1);

    public static string NameOf(int index) => Names[Normalize(index)];

    /// <summary>True when the value is already a valid index.</summary>
    public static bool IsValid(int index) => index >= 0 && index < Count;

    /// <summary>
    /// Maps a legacy hex colour onto the nearest palette index, for the one-time
    /// migration of tags and separators saved before non-negotiable #6.
    /// <para>
    /// Nearest is plain Euclidean RGB distance. That is cruder than a perceptual
    /// metric, but it only has to sort into six well-separated buckets once, and a
    /// user who dislikes the result can pick another swatch.
    /// </para>
    /// Unparseable or missing input falls back to <see cref="Blue"/>.
    /// </summary>
    public static int NearestTo(string? hex)
    {
        if (!TryParseHex(hex, out var r, out var g, out var b)) return Blue;

        var best = Blue;
        var bestDistance = int.MaxValue;

        for (var i = 0; i < MigrationReference.Length; i++)
        {
            var (rr, gg, bb) = MigrationReference[i];
            var dr = r - rr;
            var dg = g - gg;
            var db = b - bb;
            var distance = (dr * dr) + (dg * dg) + (db * db);

            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c> and <c>#AARRGGBB</c>, with or without the hash.</summary>
    private static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 8) s = s[2..]; // drop the alpha channel

        if (s.Length == 3)
        {
            // #RGB is shorthand for #RRGGBB.
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }

        if (s.Length != 6) return false;

        return byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }
}
