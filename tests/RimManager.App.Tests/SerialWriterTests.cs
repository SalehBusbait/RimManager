using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimManager.App.Services;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The save-on-every-edit writer. This exists because the same bug was written twice, and
/// each of these tests pins one half of what went wrong.
/// </summary>
public sealed class SerialWriterTests
{
    private sealed class Box(string value)
    {
        public string Value { get; } = value;
    }

    /// <summary>
    /// The loud failure: two writes to <c>tags.json</c> overlapped inside
    /// <c>File.Replace</c> — "Unable to remove the file to be replaced" — and killed the
    /// app. Nothing may run concurrently.
    /// </summary>
    [Fact]
    public async Task Writes_never_overlap()
    {
        var inFlight = 0;
        var overlapped = false;

        var writer = new SerialWriter<Box>(async _ =>
        {
            if (Interlocked.Increment(ref inFlight) > 1) overlapped = true;
            await Task.Delay(15);
            Interlocked.Decrement(ref inFlight);
        });

        for (var i = 0; i < 20; i++) writer.Queue(new Box($"v{i}"));
        await writer.DrainAsync();

        overlapped.Should().BeFalse("a second concurrent File.Replace on one path throws");
    }

    /// <summary>
    /// The silent failure: five preference changes issued five concurrent writes and the
    /// snapshot from the SECOND completed last, so the file held stale values while the UI
    /// showed the new ones. The newest value must land last.
    /// </summary>
    [Fact]
    public async Task The_last_value_queued_is_the_last_one_written()
    {
        var written = new List<string>();

        var writer = new SerialWriter<Box>(async box =>
        {
            await Task.Delay(5);
            lock (written) written.Add(box.Value);
        });

        for (var i = 0; i < 10; i++) writer.Queue(new Box($"v{i}"));
        await writer.DrainAsync();

        written.Should().NotBeEmpty();
        written[^1].Should().Be("v9", "whatever else is skipped, the newest state must win");
    }

    /// <summary>
    /// Intermediate values may be coalesced — that is the point of latest-wins — but the
    /// order of what does get written must never invert.
    /// </summary>
    [Fact]
    public async Task Written_values_never_go_backwards()
    {
        var written = new List<int>();

        var writer = new SerialWriter<Box>(async box =>
        {
            await Task.Delay(3);
            lock (written) written.Add(int.Parse(box.Value));
        });

        for (var i = 0; i < 15; i++) writer.Queue(new Box(i.ToString()));
        await writer.DrainAsync();

        written.Should().BeInAscendingOrder();
    }

    /// <summary>
    /// A failing write must be REPORTED, never thrown. It runs on a task nobody awaits, so
    /// any escape is a process kill — which is exactly how the tag save crashed the app.
    /// </summary>
    [Fact]
    public async Task A_failing_write_is_reported_and_does_not_escape()
    {
        Exception? reported = null;

        var writer = new SerialWriter<Box>(
            _ => throw new System.IO.IOException("Unable to remove the file to be replaced."),
            ex => reported = ex);

        writer.Queue(new Box("x"));
        await writer.DrainAsync();

        reported.Should().BeOfType<System.IO.IOException>();
    }

    /// <summary>One failure must not wedge the gate and stop every later save.</summary>
    [Fact]
    public async Task A_failure_does_not_stop_the_next_write()
    {
        var succeeded = 0;
        var first = true;

        var writer = new SerialWriter<Box>(_ =>
        {
            if (first) { first = false; throw new System.IO.IOException("boom"); }
            Interlocked.Increment(ref succeeded);
            return Task.CompletedTask;
        }, _ => { });

        writer.Queue(new Box("a"));
        await writer.DrainAsync();
        writer.Queue(new Box("b"));
        await writer.DrainAsync();

        succeeded.Should().Be(1, "the semaphore has to be released on the failure path too");
    }
}
