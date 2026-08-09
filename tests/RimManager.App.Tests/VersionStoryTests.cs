using System.IO;
using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The version story (N9): a deliberate version at the one source, surfaced with the
/// commit everywhere someone would paste from.
/// </summary>
public sealed class VersionStoryTests
{
    /// <summary>
    /// Deleting the Version property does not remove the version — it silently reverts
    /// every assembly to the SDK's default 1.0.0, which still passes every format
    /// check. Only a pin on the source can see the difference between a chosen number
    /// and a defaulted one.
    /// </summary>
    [Fact]
    public void The_version_is_declared_at_the_one_source()
    {
        var props = File.ReadAllText(Path.Combine(RepoPaths.Root, "Directory.Build.props"));

        props.Should().Contain("<Version>",
            "without it the SDK stamps 1.0.0 — a version nobody chose, on an app "
            + "that has not released");
    }

    /// <summary>
    /// End to end: props version → SDK commit stamp → BuildStamp → the About line.
    /// This is the line people paste into bug reports; between releases every build
    /// shares one version number, so the commit is the identifier.
    /// </summary>
    [Fact]
    public void About_names_the_version_and_the_commit()
    {
        var about = new AboutViewModel();

        // The suffix is optional in the pattern, not required: this has to keep passing
        // when the beta line ends and the version becomes a plain 1.0.0. BuildStamp
        // splits on the first '+', so SemVer's pre-release part stays with the version
        // and only the commit metadata is taken as the sha.
        about.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)? \([0-9a-f]{7}\)$");
        about.VersionLine.Should().StartWith(about.Version,
            "the copyable line leads with the build's identity");
    }

    // --- N12 · the release facts -------------------------------------------

    /// <summary>
    /// The licence has to exist as a FILE. Declaring `PackageLicenseExpression` alone
    /// tells NuGet and nothing else — GitHub, forks and anyone reading the tree look
    /// for LICENSE at the root, and its absence is what makes a repository "all rights
    /// reserved" no matter what the metadata claims.
    /// </summary>
    [Fact]
    public void The_repository_carries_the_licence_as_a_file()
    {
        var licence = Path.Combine(RepoPaths.Root, "LICENSE");

        File.Exists(licence).Should().BeTrue();
        File.ReadAllText(licence).Should().Contain("MIT License")
            .And.Contain("WITHOUT WARRANTY OF ANY KIND",
                "the warranty disclaimer is the half that protects the author");
    }

    /// <summary>
    /// The build must carry the same licence the file states. Two places naming a
    /// licence is two places for them to disagree, and the metadata is the one a
    /// package consumer reads without ever seeing the repository.
    /// </summary>
    [Fact]
    public void The_build_declares_the_same_licence()
    {
        var props = File.ReadAllText(Path.Combine(RepoPaths.Root, "Directory.Build.props"));

        props.Should().Contain("<PackageLicenseExpression>MIT</PackageLicenseExpression>");
        props.Should().Contain("<Copyright>");
    }

    /// <summary>
    /// Every dependency that ships has to be named somewhere a redistributor can find
    /// it. MIT obliges US to pass on OUR notice; it does not dissolve theirs.
    /// </summary>
    [Fact]
    public void Shipped_dependencies_are_credited()
    {
        var notices = File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.md"));

        foreach (var shipped in new[]
                 { "Avalonia", "CommunityToolkit.Mvvm", "Mono.Cecil", "Microsoft.Data.Sqlite", "System.CommandLine" })
        {
            notices.Should().Contain(shipped, $"{shipped} ships in the build");
        }

        notices.Should().Contain("FluentAssertions",
            "its 7.x pin is a licensing decision, and the reason has to survive the person who made it");
    }

    /// <summary>
    /// The community rules database carries NO licence — no LICENSE file, nothing in
    /// GitHub's metadata — so by default nobody may redistribute it. Fetching it at
    /// runtime to the user's own machine is fine and is what the database exists for;
    /// SHIPPING a copy would not be.
    /// <para>
    /// This guards the difference. The committed fixture is hand-written synthetic data
    /// exercising the format — three invented packageIds — and it stays small: a real
    /// snapshot is thousands of rules, so a size ceiling catches the one mistake that
    /// matters (someone dropping a live download in as a "better" fixture) without
    /// pinning contents that may legitimately change.
    /// </para>
    /// </summary>
    [Fact]
    public void No_community_rules_data_is_redistributed()
    {
        var fixture = Path.Combine(RepoPaths.Root, "fixtures", "community", "communityRules.json");

        // No fixture means nothing is redistributed, which is the invariant satisfied
        // rather than violated. The other fixture-dependent tests skip when the folder
        // is absent; this one used to be the single failure in that configuration.
        if (!File.Exists(fixture)) return;

        new FileInfo(fixture).Length.Should().BeLessThan(8_000,
            "the rules fixture must stay synthetic — the upstream database is unlicensed, "
            + "so committing a copy of it would redistribute material nobody has licensed");

        var json = File.ReadAllText(fixture);
        json.Should().Contain("some.",
            "the invented packageIds are what make this demonstrably not a real snapshot");
    }

    /// <summary>
    /// About is the only place a user is told what they may do with this. It names the
    /// licence rather than reproducing it, and says the dependencies keep their own —
    /// the half people get wrong about MIT.
    /// </summary>
    [Fact]
    public void About_states_the_licence()
    {
        AboutViewModel.LicenceText.Should().Contain("MIT");
        AboutViewModel.LicenceText.Should().Contain("THIRD-PARTY-NOTICES",
            "naming the file is what makes the dependency licences findable");
    }
}
