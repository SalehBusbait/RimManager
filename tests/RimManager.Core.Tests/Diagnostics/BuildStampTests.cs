using FluentAssertions;
using RimManager.Core.Diagnostics;
using Xunit;

namespace RimManager.Core.Tests.Diagnostics;

/// <summary>
/// The build stamp (N9): the string every "what are you running" answer is built from.
/// </summary>
public sealed class BuildStampTests
{
    [Fact]
    public void A_full_informational_version_becomes_version_and_short_commit()
    {
        BuildStamp.Describe("0.9.0+a0d6b62def31567a3d07ad15dae89e3423fec743")
            .Should().Be("0.9.0 (a0d6b62)",
                "seven characters is what git log --oneline prints, so the report "
                + "reads directly against the history");
    }

    [Fact]
    public void A_version_without_metadata_stands_alone()
    {
        BuildStamp.Describe("0.9.0").Should().Be("0.9.0",
            "a build without git produces no commit, and inventing brackets around "
            + "nothing would imply one exists");
    }

    [Fact]
    public void Short_metadata_is_kept_whole()
    {
        BuildStamp.Describe("0.9.0+abc").Should().Be("0.9.0 (abc)");
    }

    /// <summary>
    /// N12 ships a SemVer PRE-RELEASE ("1.0.0-beta.1"), and this is the shape the app
    /// actually renders now. The split is on the first '+', so the suffix belongs to
    /// the version and only the commit metadata becomes the sha — a split on '-' would
    /// have torn the version in half and reported "1.0.0" for a beta build.
    /// </summary>
    [Fact]
    public void A_pre_release_version_keeps_its_suffix()
    {
        BuildStamp.Describe("1.0.0-beta.1+a0d6b62def31567a3d07ad15dae89e3423fec743")
            .Should().Be("1.0.0-beta.1 (a0d6b62)");
    }

    [Fact]
    public void A_pre_release_without_metadata_stands_alone()
    {
        BuildStamp.Describe("1.0.0-beta.1").Should().Be("1.0.0-beta.1");
    }

    [Fact]
    public void Nothing_in_means_null_out()
    {
        BuildStamp.Describe(null).Should().BeNull();
        BuildStamp.Describe("").Should().BeNull();
        BuildStamp.Describe("   ").Should().BeNull();
    }

    [Fact]
    public void The_test_assembly_itself_carries_a_stamped_build()
    {
        // End to end through the real build: Directory.Build.props supplies the
        // version, the SDK stamps the commit, ForAssembly renders both. Fails on a
        // source-tarball build with no .git — deliberately, because git IS a build
        // input here: without it every binary claims to be every other binary.
        // The pre-release part is optional (N12: "1.0.0-beta.1"), so this keeps passing
        // when the beta line ends. Describe splits on the first '+', which is why the
        // SemVer suffix stays with the version and only the metadata becomes the sha.
        BuildStamp.ForAssembly(typeof(BuildStampTests).Assembly)
            .Should().MatchRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)? \([0-9a-f]{7}\)$");
    }
}
