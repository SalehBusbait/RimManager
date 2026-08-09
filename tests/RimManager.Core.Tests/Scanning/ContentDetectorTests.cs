using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Scanning;

public sealed class ContentDetectorTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-07-24T00:00:00Z")));

    [Fact]
    public void Detects_content_at_root()
    {
        var fs = Fs();
        fs.AddFile("/mods/x/Defs/things.xml", "<Defs/>");
        fs.AddFile("/mods/x/Textures/a.png", "img");

        var flags = ContentDetector.Detect(fs, "/mods/x");

        flags.Should().HaveFlag(ContentFlags.Defs);
        flags.Should().HaveFlag(ContentFlags.Textures);
        flags.Should().NotHaveFlag(ContentFlags.Assemblies);
    }

    [Fact]
    public void Detects_assemblies_only_when_a_dll_is_present()
    {
        var fs = Fs();
        fs.AddFile("/mods/x/Assemblies/readme.txt", "not a dll");
        ContentDetector.Detect(fs, "/mods/x").Should().NotHaveFlag(ContentFlags.Assemblies);

        fs.AddFile("/mods/x/Assemblies/Mod.dll", "MZ");
        ContentDetector.Detect(fs, "/mods/x").Should().HaveFlag(ContentFlags.Assemblies);
    }

    [Fact]
    public void Unions_the_active_version_subfolder()
    {
        var fs = Fs();
        fs.AddFile("/mods/x/About/About.xml", "<ModMetaData/>");
        fs.AddFile("/mods/x/1.6/Assemblies/Mod.dll", "MZ");

        ContentDetector.Detect(fs, "/mods/x", "1.6").Should().HaveFlag(ContentFlags.Assemblies);
        // A different active version must not pick up the 1.6 assemblies.
        ContentDetector.Detect(fs, "/mods/x", "1.5").Should().NotHaveFlag(ContentFlags.Assemblies);
    }

    [Fact]
    public void LoadFolders_redirects_detection_to_named_folders()
    {
        // Harmony-style: 1.6 loads root + "Current", where the dll actually lives.
        var fs = Fs();
        fs.AddFile("/mods/harmony/LoadFolders.xml", """
            <loadFolders>
              <v1.4><li>/</li><li>1.4</li></v1.4>
              <v1.6><li>/</li><li>Current</li></v1.6>
            </loadFolders>
            """);
        fs.AddFile("/mods/harmony/Current/Assemblies/0Harmony.dll", "MZ");
        fs.AddFile("/mods/harmony/1.4/Assemblies/0Harmony.dll", "MZ");

        ContentDetector.Detect(fs, "/mods/harmony", "1.6").Should().HaveFlag(ContentFlags.Assemblies);
    }

    [Fact]
    public void LoadFolders_is_authoritative_and_can_exclude_root()
    {
        // v1.6 lists ONLY "Current" (not "/"), so root Defs must be ignored.
        var fs = Fs();
        fs.AddFile("/mods/x/LoadFolders.xml", "<loadFolders><v1.6><li>Current</li></v1.6></loadFolders>");
        fs.AddFile("/mods/x/Defs/root.xml", "<Defs/>");
        fs.AddFile("/mods/x/Current/Assemblies/Mod.dll", "MZ");

        var flags = ContentDetector.Detect(fs, "/mods/x", "1.6");

        flags.Should().HaveFlag(ContentFlags.Assemblies);
        flags.Should().NotHaveFlag(ContentFlags.Defs, "root is not listed for v1.6");
    }

    /// <summary>NF-10 · the Workshop-item-as-mod-list shape: the payload sits at the
    /// root, and only the root — version subfolders never carry one.</summary>
    [Fact]
    public void A_rwlist_at_the_root_is_a_fact_and_one_in_a_subfolder_is_not()
    {
        var fs = Fs();
        fs.AddFile("/mods/list/About/About.xml", "<ModMetaData/>");
        fs.AddFile("/mods/list/My Colony List.rwlist", "{}");

        ContentDetector.Detect(fs, "/mods/list").Should().HaveFlag(ContentFlags.RwList);

        var nested = Fs();
        nested.AddFile("/mods/x/About/About.xml", "<ModMetaData/>");
        nested.AddFile("/mods/x/1.6/bundle.rwlist", "{}");

        ContentDetector.Detect(nested, "/mods/x", "1.6")
            .Should().NotHaveFlag(ContentFlags.RwList, "the defined shape is root-only");
    }
}
