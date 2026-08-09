using FluentAssertions;
using RimManager.Core.Scanning;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests;

public class FolderSizeTests
{
    private static InMemoryFileSystem Fs() => new(new FixedClock(DateTimeOffset.UnixEpoch));

    [Fact]
    public void Adds_up_every_file_under_the_root()
    {
        var fs = Fs();
        fs.AddFile("/mods/Hats/About/About.xml", new string('a', 100));
        fs.AddFile("/mods/Hats/Textures/hat.png", new string('b', 2_000));
        fs.AddFile("/mods/Hats/Defs/Deep/Nested/thing.xml", new string('c', 50));

        FolderSize.Bytes(fs, "/mods/Hats").Should().Be(2_150);
    }

    [Fact]
    public void Counts_nothing_from_a_sibling_mod()
    {
        var fs = Fs();
        fs.AddFile("/mods/Hats/a.txt", new string('a', 10));
        fs.AddFile("/mods/Boots/b.txt", new string('b', 999));

        FolderSize.Bytes(fs, "/mods/Hats").Should().Be(10);
    }

    [Fact]
    public void A_missing_folder_is_zero_rather_than_a_throw()
    {
        // The info pane asks about whatever row is selected; a mod deleted under a
        // running RimManager must not take the pane down with it.
        FolderSize.Bytes(Fs(), "/mods/Gone").Should().Be(0);
    }

    [Fact]
    public void An_empty_root_is_zero()
    {
        var fs = Fs();
        fs.CreateDirectory("/mods/Empty");

        FolderSize.Bytes(fs, "/mods/Empty").Should().Be(0);
    }

    [Fact]
    public void A_blank_path_is_zero()
    {
        FolderSize.Bytes(Fs(), "").Should().Be(0);
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        var fs = Fs();
        fs.AddFile("/mods/Hats/a.txt", "a");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var walk = () => FolderSize.Bytes(fs, "/mods/Hats", cts.Token);
        walk.Should().Throw<OperationCanceledException>();
    }
}
