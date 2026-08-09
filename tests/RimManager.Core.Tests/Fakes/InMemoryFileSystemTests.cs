using FluentAssertions;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Fakes;

/// <summary>The test double is itself covered so tests can trust it.</summary>
public sealed class InMemoryFileSystemTests
{
    private static InMemoryFileSystem New() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-07-24T00:00:00Z")));

    [Fact]
    public void Added_file_is_readable_and_parent_dirs_exist()
    {
        var fs = New();
        fs.AddFile("/mods/harmony/About/About.xml", "<ModMetaData />");

        fs.FileExists("/mods/harmony/About/About.xml").Should().BeTrue();
        fs.ReadAllText("/mods/harmony/About/About.xml").Should().Be("<ModMetaData />");
        fs.DirectoryExists("/mods/harmony/About").Should().BeTrue();
        fs.DirectoryExists("/mods").Should().BeTrue();
    }

    [Fact]
    public void Enumerate_returns_immediate_children_only()
    {
        var fs = New();
        fs.AddFile("/root/a.txt", "a");
        fs.AddFile("/root/sub/b.txt", "b");

        var names = fs.EnumerateEntries("/root").Select(e => e.FullPath).ToList();

        names.Should().Contain("/root/a.txt");
        names.Should().Contain("/root/sub");
        names.Should().NotContain("/root/sub/b.txt");
    }

    [Fact]
    public void Enumerate_missing_directory_is_empty_not_throwing()
    {
        New().EnumerateEntries("/nope").Should().BeEmpty();
    }
}
