using System.Text;
using FluentAssertions;
using RimManager.Core.Abstractions;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Exercises the real disk-backed atomic write. Self-contained: each test uses a
/// unique temp directory and cleans up, so it is CI-safe on all three platforms.
/// </summary>
public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _dir;

    public PhysicalFileSystemTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rimmanager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    private static ReadOnlyMemory<byte> Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task AtomicWrite_creates_new_file_without_backup()
    {
        var fs = new PhysicalFileSystem();
        var path = Path.Combine(_dir, "ModsConfig.xml");

        await fs.AtomicWriteAsync(path, Bytes("<v1/>"));

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("<v1/>");
        Directory.GetFiles(_dir, "*.bak").Should().BeEmpty("nothing existed to back up");
    }

    [Fact]
    public async Task AtomicWrite_backs_up_previous_contents_on_overwrite()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-07-24T12:00:00Z"));
        var fs = new PhysicalFileSystem(clock);
        var path = Path.Combine(_dir, "ModsConfig.xml");

        await fs.AtomicWriteAsync(path, Bytes("<v1/>"));
        await fs.AtomicWriteAsync(path, Bytes("<v2/>"));

        (await File.ReadAllTextAsync(path)).Should().Be("<v2/>");

        var backups = Directory.GetFiles(_dir, "*.bak");
        backups.Should().ContainSingle("the pre-overwrite contents must be preserved");
        (await File.ReadAllTextAsync(backups[0])).Should().Be("<v1/>");
    }

    [Fact]
    public async Task AtomicWrite_leaves_no_temp_files_behind()
    {
        var fs = new PhysicalFileSystem();
        var path = Path.Combine(_dir, "ModsConfig.xml");

        await fs.AtomicWriteAsync(path, Bytes("<v1/>"), backup: false);

        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
