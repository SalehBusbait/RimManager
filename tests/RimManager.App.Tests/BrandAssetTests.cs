using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The app mark is referenced by URI from About, both first-run marks and the main
/// window's icon. A URI that does not resolve makes <c>Image.Source</c> null, and a
/// null source renders <b>nothing at all</b> — no exception, no warning, no failing
/// test. Same silent family as the tag stripe and the dead Fetch button.
/// <para>
/// These check the file behind every <c>avares://</c> reference rather than asking
/// Avalonia to resolve it: <c>AssetLoader</c> needs an initialised AppBuilder, and
/// standing a headless Avalonia app up for the test project is its own piece of work.
/// What that costs is that a missing <c>AvaloniaResource</c> glob would still pass, so
/// the csproj entry is pinned separately below.
/// </para>
/// </summary>
public sealed class BrandAssetTests
{
    private static string AppResourcePath(string uri) =>
        Path.Combine(RepoPaths.AppProject,
            uri["avares://RimManager/".Length..].Replace('/', Path.DirectorySeparatorChar));

    private static IEnumerable<(string File, string Uri)> References()
    {
        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"avares://RimManager/[^""'\s]+"))
            {
                yield return (Path.GetFileName(file), m.Value);
            }
        }
    }

    /// <summary>
    /// Every asset URI in every view points at a file that is actually there. This
    /// covers the theme includes as well as the artwork, so a renamed dictionary is
    /// caught by the same net.
    /// </summary>
    [Fact]
    public void Every_avares_reference_points_at_a_real_file()
    {
        var refs = References().ToList();
        refs.Should().NotBeEmpty("the app mark and the theme dictionaries are referenced this way");

        foreach (var (file, uri) in refs)
        {
            File.Exists(AppResourcePath(uri)).Should().BeTrue(
                $"{file} references {uri}, which resolves to no file — the control bound to "
                + "it would render empty and nothing would fail");
        }
    }

    /// <summary>Without the glob the artwork is on disk but not in the assembly.</summary>
    [Fact]
    public void The_assets_folder_is_shipped_as_an_Avalonia_resource() =>
        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "RimManager.App.csproj"))
            .Should().Contain("<AvaloniaResource Include=\"Assets\\**\" />");

    /// <summary>
    /// The largest use is the 64px welcome hero, which at 200% DPI wants 128 real
    /// pixels. A smaller master would resample upward and go soft on exactly the screen
    /// the logo is there to introduce the app on.
    /// </summary>
    [Fact]
    public void The_master_is_big_enough_for_every_use()
    {
        var png = File.ReadAllBytes(Path.Combine(RepoPaths.AppProject, "Assets", "app-mark.png"));

        // PNG: 8-byte signature, then the IHDR chunk whose data starts at byte 16.
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));

        width.Should().BeGreaterThanOrEqualTo(128);
        height.Should().BeGreaterThanOrEqualTo(128);
    }

    /// <summary>
    /// The artwork and its generator are committed, so the icon can be rebuilt rather
    /// than being a binary nobody can reproduce.
    /// </summary>
    [Fact]
    public void The_artwork_and_its_generator_are_committed()
    {
        var brand = Path.Combine(RepoPaths.Root, "assets", "brand");

        foreach (var file in new[] { "rimmanager-logo.svg", "make-icon.py", "rimmanager.ico" })
        {
            File.Exists(Path.Combine(brand, file)).Should().BeTrue($"assets/brand/{file} is missing");
        }
    }
}
