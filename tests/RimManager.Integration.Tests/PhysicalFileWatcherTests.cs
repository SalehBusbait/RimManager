using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// The watcher that makes the drift verdict live. Real disk in a temp directory, because
/// the whole point of this class is the platform's own file notifications — a fake would
/// test the fake.
/// </summary>
public sealed class PhysicalFileWatcherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rm-watch-").FullName;
    private readonly PhysicalFileWatcher _watcher = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    /// <summary>Waits for the debounce plus slack, so a coalesced callback has landed.</summary>
    private static void Settle() => Thread.Sleep(900);

    [Fact]
    public void An_in_place_write_is_noticed()
    {
        var file = Path("ModsConfig.xml");
        File.WriteAllText(file, "<one/>");

        var fired = 0;
        using var _ = _watcher.Watch(file, () => Interlocked.Increment(ref fired));

        File.WriteAllText(file, "<two/>");
        Settle();

        fired.Should().BeGreaterThan(0, "the game rewriting its mod list is the case this exists for");
    }

    /// <summary>
    /// The interface promises coalescing, and it has to: one save fires the platform
    /// watcher two or three times, and this project's own atomic write is a temp file, a
    /// copy and a replace — so a burst per write is the normal case, not the edge.
    /// </summary>
    [Fact]
    public void A_burst_of_writes_produces_one_callback()
    {
        var file = Path("ModsConfig.xml");
        File.WriteAllText(file, "<start/>");

        var fired = 0;
        using var _ = _watcher.Watch(file, () => Interlocked.Increment(ref fired));

        for (var i = 0; i < 8; i++)
        {
            File.WriteAllText(file, $"<n>{i}</n>");
            Thread.Sleep(20);
        }

        Settle();

        fired.Should().Be(1, "a caller that has to de-duplicate is a caller that will forget to");
    }

    /// <summary>
    /// Replace-in-place, which is how BOTH RimWorld and our own AtomicWriteAsync write.
    /// A watcher bound to the file rather than to its directory reports the first change
    /// and then goes silent for ever — the failure this class is shaped to avoid.
    /// </summary>
    [Fact]
    public void A_replaced_file_is_still_watched_afterwards()
    {
        var file = Path("ModsConfig.xml");
        var temp = Path("tmp.xml");
        File.WriteAllText(file, "<original/>");

        var fired = 0;
        using var _ = _watcher.Watch(file, () => Interlocked.Increment(ref fired));

        File.WriteAllText(temp, "<replacement/>");
        File.Replace(temp, file, destinationBackupFileName: null);
        Settle();

        var afterReplace = fired;
        afterReplace.Should().BeGreaterThan(0);

        // The real test: it is still listening.
        File.WriteAllText(file, "<later/>");
        Settle();

        fired.Should().BeGreaterThan(afterReplace,
            "a file-bound watcher dies on the first replace, and replace is how the game writes");
    }

    [Fact]
    public void Nothing_fires_for_a_different_file_in_the_same_folder()
    {
        var file = Path("ModsConfig.xml");
        File.WriteAllText(file, "<one/>");

        var fired = 0;
        using var _ = _watcher.Watch(file, () => Interlocked.Increment(ref fired));

        File.WriteAllText(Path("Prefs.xml"), "<other/>");
        Settle();

        fired.Should().Be(0, "the config folder holds every Mod_*.xml settings file too");
    }

    [Fact]
    public void Disposing_stops_the_callbacks()
    {
        var file = Path("ModsConfig.xml");
        File.WriteAllText(file, "<one/>");

        var fired = 0;
        var subscription = _watcher.Watch(file, () => Interlocked.Increment(ref fired));
        subscription.Dispose();

        File.WriteAllText(file, "<two/>");
        Settle();

        fired.Should().Be(0);
    }

    /// <summary>
    /// A half-configured install has no config folder yet, and the app still has to start.
    /// Watching nothing is a state, not a failure.
    /// </summary>
    [Fact]
    public void A_missing_folder_is_not_an_error()
    {
        var act = () => _watcher.Watch(Path("nope/ModsConfig.xml"), () => { });

        act.Should().NotThrow();
        act().Should().NotBeNull("the caller disposes whatever it is given");
    }
}
