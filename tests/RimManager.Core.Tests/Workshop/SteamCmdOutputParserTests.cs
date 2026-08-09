using FluentAssertions;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class SteamCmdOutputParserTests
{
    // Verbatim from a real anonymous download (note the self-update chatter after the
    // result line, and that "Success…" has no trailing newline before "Unloading").
    private const string RealSuccess = """
        Connecting anonymously to Steam Public...OK
        Waiting for user info...OK
        Downloading item 3346964576 ...
        Success. Downloaded item 3346964576 to "c:\steamcmd\steamapps\workshop\content\294100\3346964576" (89783 bytes) Unloading Steam API...OK
        [  0%] Checking for available updates...
        [100%] Download complete.
        """;

    [Fact]
    public void Parses_a_real_success_line_amid_self_update_noise()
    {
        var result = SteamCmdOutputParser.ResultFor(RealSuccess, "3346964576")!;

        result.Success.Should().BeTrue();
        result.Bytes.Should().Be(89783);
        result.DownloadedPath.Should().EndWith(@"content\294100\3346964576");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Parses_a_failure_line()
    {
        var result = SteamCmdOutputParser.ResultFor("ERROR! Download item 999 failed (Timeout).", "999")!;

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Timeout");
        result.DownloadedPath.Should().BeNull();
    }

    [Fact]
    public void Parses_mixed_success_and_failure_across_items()
    {
        const string output = """
            Downloading item 111 ...
            Success. Downloaded item 111 to "/x/111" (1024 bytes)
            Downloading item 222 ...
            ERROR! Download item 222 failed (Failure).
            """;

        var results = SteamCmdOutputParser.Parse(output);
        results.Should().HaveCount(2);
        results.Single(r => r.PublishedFileId == "111").Success.Should().BeTrue();
        results.Single(r => r.PublishedFileId == "222").Success.Should().BeFalse();
    }

    [Fact]
    public void Success_overrides_an_earlier_failure_for_the_same_id_on_retry()
    {
        const string output = """
            ERROR! Download item 111 failed (Failure).
            Success. Downloaded item 111 to "/x/111" (2048 bytes)
            """;

        var result = SteamCmdOutputParser.ResultFor(output, "111")!;
        result.Success.Should().BeTrue();
        result.Bytes.Should().Be(2048);
    }

    [Fact]
    public void Empty_output_yields_no_results()
    {
        SteamCmdOutputParser.Parse("").Should().BeEmpty();
        SteamCmdOutputParser.ResultFor("nothing here", "1").Should().BeNull();
    }
}
