using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The two build-time guards the design handoff asks for
/// (AVALONIA_GUIDE.md §0.1 and §2).
/// </summary>
public sealed class ThemeTokenTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>One generated dictionary per catalog theme; the enum member IS the
    /// file name, so a theme added without its file fails here by name.</summary>
    private static string[] TokenFiles =>
        [.. RimManager.App.Themes.ThemeCatalog.All.Select(t => $"Tokens.{t.Theme}.axaml")];

    /// <summary>
    /// The Fluent keys each dictionary carries for the un-retemplated controls
    /// (Slider, plain RadioButton). The retired runtime accent derivation used to
    /// write these at application level; the dictionaries author them now.
    /// </summary>
    private static readonly HashSet<string> FluentAccentKeys =
    [
        "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
        "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
        "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver",
        "RadioButtonOuterEllipseCheckedFillPressed", "RadioButtonOuterEllipseCheckedStroke",
        "RadioButtonCheckGlyphFill",
    ];

    private static HashSet<string> KeysOf(string fileName)
    {
        var path = Path.Combine(RepoPaths.Themes, fileName);
        File.Exists(path).Should().BeTrue($"{fileName} must exist");

        return [.. XDocument.Load(path).Descendants()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)];
    }

    /// <summary>
    /// Every theme is a full citizen (amended non-negotiable #9): all ten generated
    /// dictionaries declare the identical key set. This is the cheapest possible
    /// guard against the "one theme has a black-on-black row" class of bug.
    /// </summary>
    [Fact]
    public void All_theme_dictionaries_declare_identical_token_keys()
    {
        var files = TokenFiles;
        files.Should().HaveCount(10, "the roster is ten themes");

        var reference = KeysOf(files[0]);
        reference.Should().NotBeEmpty();

        foreach (var file in files.Skip(1))
        {
            var keys = KeysOf(file);
            keys.Except(reference).Order().ToArray().Should().BeEmpty(
                $"{file} declares keys the reference set lacks");
            reference.Except(keys).Order().ToArray().Should().BeEmpty(
                $"{file} is missing keys the reference set declares");
        }
    }

    /// <summary>Every token is an Rm-prefixed key — except the declared Fluent keys
    /// for the controls we never re-templated; a stray naming convention makes the
    /// grep guard below unenforceable.</summary>
    [Fact]
    public void Every_token_key_uses_the_Rm_prefix()
    {
        foreach (var key in KeysOf(TokenFiles[0]).Where(k => !FluentAccentKeys.Contains(k)))
            key.Should().StartWith("Rm");
    }

    /// <summary>
    /// The keys the v2 pass retired must stay gone: the accent-derivation fixed
    /// point, the single brand amber (now per-theme RmBrandBrush) and the unused
    /// shadow brush. A retired key returning means a second source of truth.
    /// </summary>
    [Fact]
    public void Retired_keys_stay_retired_and_new_keys_exist()
    {
        var keys = KeysOf(TokenFiles[0]);

        keys.Should().NotContain("RmAccentBaseBrush", "the runtime accent derivation is gone");
        keys.Should().NotContain("RmBrandAmberBrush", "brand is per-theme RmBrandBrush now");
        keys.Should().NotContain("RmShadowBrush", "folded into RmDrawerShadow, the one composed shadow");

        foreach (var added in new[]
        {
            "RmBrandBrush", "RmFocusRingBrush", "RmInfoBrush", "RmInfoTintBrush",
            "RmHarmonyBrush", "RmHarmonyTintBrush",
        })
        {
            keys.Should().Contain(added, "the v2 token additions are load-bearing");
        }
    }

    /// <summary>
    /// The six-hue tag/separator palette is addressed by index, because tags and
    /// separators persist a palette index and never a hex string (non-negotiable
    /// #6) — that is what makes user colours flip with the theme. v2 adds a
    /// bg pair per hue for the row tag pills.
    /// </summary>
    [Fact]
    public void Palette_is_index_addressed_and_six_hues_wide()
    {
        var dark = KeysOf(TokenFiles[0]);

        for (var i = 0; i <= 5; i++)
        {
            dark.Should().Contain($"RmPalette{i}Brush");
            dark.Should().Contain($"RmPalette{i}BgBrush");
        }

        dark.Should().NotContain("RmPalette6Brush", "the palette is exactly six hues");
    }

    /// <summary>Each ModSource needs a foreground/background pair — a source badge is
    /// letter AND tint, never tint alone (1f).</summary>
    [Fact]
    public void Every_source_badge_has_a_foreground_and_background_tint()
    {
        var dark = KeysOf(TokenFiles[0]);

        foreach (var source in new[] { "Core", "Dlc", "Workshop", "Local", "Git", "Pinned" })
        {
            dark.Should().Contain($"RmSrc{source}FgBrush");
            dark.Should().Contain($"RmSrc{source}BgBrush");
        }
    }

    /// <summary>
    /// "No literal #RRGGBB outside the two theme dictionaries. Everything is
    /// {DynamicResource Rm...Brush}. Add a CI grep: any # colour in Views/ fails."
    /// — AVALONIA_GUIDE.md §0.1.
    /// </summary>
    [Fact]
    public void No_literal_colours_outside_the_theme_dictionaries()
    {
        // #RGB, #RRGGBB and #AARRGGBB, but not a Grid "#" column or an XML entity.
        var hex = new Regex(@"#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            // Themes/ IS the place colours are allowed to be literal.
            if (file.StartsWith(RepoPaths.Themes, StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (var (line, number) in MarkupLines(file))
            {
                if (!hex.IsMatch(line)) continue;
                offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{number}: {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "colour belongs in the generated Tokens.<Theme>.axaml dictionaries — use a {DynamicResource Rm*Brush}");
    }

    /// <summary>
    /// Yields a file's lines with XML comments stripped out.
    /// <para>
    /// The colour and Fluent-brush guards scan MARKUP, not prose. A design note that
    /// mentions "#118" or "#4" is not a colour, and failing the build over one would
    /// train the next person to write worse comments — or to delete the guard.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Line, int Number)> MarkupLines(string file)
    {
        var inComment = false;
        var number = 0;

        foreach (var raw in File.ReadAllLines(file))
        {
            number++;
            var line = raw;

            if (inComment)
            {
                var close = line.IndexOf("-->", StringComparison.Ordinal);
                if (close < 0) continue;
                line = line[(close + 3)..];
                inComment = false;
            }

            // Strip any single-line comments, then detect an unterminated one.
            while (true)
            {
                var open = line.IndexOf("<!--", StringComparison.Ordinal);
                if (open < 0) break;

                var close = line.IndexOf("-->", open, StringComparison.Ordinal);
                if (close < 0)
                {
                    line = line[..open];
                    inComment = true;
                    break;
                }

                line = line[..open] + line[(close + 3)..];
            }

            if (line.Trim().Length > 0) yield return (line, number);
        }
    }

    /// <summary>
    /// Colour must come from OUR token set, not from Fluent's built-in palette.
    /// A <c>ThemeBorderMidBrush</c> looks like a token but is outside the light/dark
    /// pair the parity test guards, so it silently opts that element out of the
    /// design system — the same class of bug as a literal hex, just harder to spot.
    /// </summary>
    [Fact]
    public void Views_use_Rm_tokens_rather_than_Fluent_theme_brushes()
    {
        var fluent = new Regex(@"\{(?:Dynamic|Static)Resource\s+Theme\w+\}");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.StartsWith(RepoPaths.Themes, StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (var (line, number) in MarkupLines(file))
            {
                if (!fluent.IsMatch(line)) continue;
                offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{number}: {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty("use an Rm*Brush token — Fluent's own brushes bypass the light/dark parity guard");
    }

    /// <summary>
    /// Compact 20px is the default density and comfortable is 26px — nothing
    /// between (non-negotiable #10). RmRowHeight is the one swappable resource
    /// that re-lays out every list in the app.
    /// </summary>
    [Fact]
    public void Row_heights_are_the_two_specified_densities()
    {
        var path = Path.Combine(RepoPaths.Themes, "Metrics.axaml");
        var doc = XDocument.Load(path);

        string Value(string key) =>
            doc.Descendants().First(e => (string?)e.Attribute(X + "Key") == key).Value.Trim();

        Value("RmRowHeightCompact").Should().Be("20");
        Value("RmRowHeightComfortable").Should().Be("26");
        Value("RmRowHeight").Should().Be("20", "compact is the default density");
        Value("RmSeparatorRowHeight").Should().Be("22", "separators are 22 regardless of density");

        // The single exception #10 does not cover, and it is not a third density: it
        // is not offered, not persisted, and not reachable from the density control.
        // 2k specifies 24 for the segmented layout, where one list does the work of
        // two. Pinned so it cannot be quietly widened into a general option.
        Value("RmRowHeightNarrow").Should().Be("24", "2k's segmented layout, below 900px");
    }

    /// <summary>
    /// There must be exactly ONE writer of <c>RmRowHeight</c>. Two produced the theme
    /// bug in R6 — a value written in one place and re-applied from another, which
    /// disagreed the first time they ran in the wrong order. The narrow row height for
    /// 2k tempted a second writer in the window's resize handler.
    /// </summary>
    [Fact]
    public void Only_one_place_writes_the_row_height_resource()
    {
        var writers = Directory
            .EnumerateFiles(RepoPaths.AppProject, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("Resources[\"RmRowHeight\"]", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToArray();

        writers.Should().ContainSingle(
            "density and the 2k breakpoint must resolve to one height in one place; "
            + $"found writers in: {string.Join(", ", writers)}");
    }
}
