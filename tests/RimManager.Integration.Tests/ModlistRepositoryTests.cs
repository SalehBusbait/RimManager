using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Storage;
using RimManager.Storage.Repositories;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Modlists round-tripped through real files, and the "exactly one undeletable default"
/// invariant enforced where it actually has to hold — against a directory that anything
/// could have edited between launches.
/// </summary>
public sealed class ModlistRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rimmanager-modlists-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly PhysicalFileSystem _fs = new();

    public ModlistRepositoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private ModlistRepository Repo() => new(_fs, _dir);

    private static ModlistState TwoMods() => ModlistState.Empty.WithEntries(
    [
        ModlistEntry.Separator("sep1", "Frameworks", paletteIndex: 2),
        ModlistEntry.Mod(ModId.From("brrainz.harmony")),
        ModlistEntry.Mod(ModId.From("zetrith.prepatcher"), enabled: false),
    ]);

    [Fact]
    public async Task A_list_round_trips_with_its_separators_intact()
    {
        var repo = Repo();
        var created = await repo.CreateAsync("Heavily modded", TwoMods());

        var loaded = repo.Get(created.Id)!;

        loaded.Name.Should().Be("Heavily modded");
        loaded.State.Entries.Should().HaveCount(3);

        var separator = loaded.State.Entries[0];
        separator.Kind.Should().Be(ModlistEntryKind.Separator);
        separator.DisplayName.Should().Be("Frameworks");
        separator.PaletteIndex.Should().Be(2, "a separator's colour is a palette index, never a hex");

        loaded.State.ActiveModIds().Select(m => m.Value)
            .Should().ContainSingle("a disabled mod is not active")
            .Which.Should().Be("brrainz.harmony");
        loaded.State.AllModIds().Should().HaveCount(2);
    }

    /// <summary>
    /// The entry carries its own identity so an export stays correct after the mod is
    /// uninstalled — the bug this schema exists to fix.
    /// </summary>
    [Fact]
    public async Task A_mod_entry_remembers_where_it_came_from()
    {
        var repo = Repo();
        var state = ModlistState.Empty.WithEntries(
        [
            new ModlistEntry(
                ModlistEntryKind.Mod, "some.workshop.mod", "Some Workshop Mod",
                Source: ModSource.Workshop, PublishedFileId: "2009463077", ModVersion: "1.4.2"),
        ]);

        var created = await repo.CreateAsync("Shared", state);
        var entry = repo.Get(created.Id)!.State.Entries[0];

        entry.Source.Should().Be(ModSource.Workshop);
        entry.PublishedFileId.Should().Be("2009463077",
            "without this an export of an uninstalled mod is uninstallable by whoever receives it");
        entry.ModVersion.Should().Be("1.4.2");
    }

    [Fact]
    public async Task An_empty_directory_seeds_exactly_one_default()
    {
        var repo = Repo();

        var lists = await repo.EnsureDefaultAsync(TwoMods);

        lists.Should().ContainSingle();
        lists[0].IsDefault.Should().BeTrue();
        lists[0].Name.Should().Be("Default");
        lists[0].State.Entries.Should().HaveCount(3, "the seed adopts the game's current list");
    }

    /// <summary>
    /// The steady state runs on every load, so it must not rewrite files each time —
    /// that is a write amplification bug and a backup churn bug at once.
    /// </summary>
    [Fact]
    public async Task Ensuring_a_healthy_set_writes_nothing()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);

        var before = Directory.GetFiles(repo.ModlistsDirectory)
            .ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));

        await Task.Delay(20);
        await repo.EnsureDefaultAsync(TwoMods);

        foreach (var (file, stamp) in before)
            File.GetLastWriteTimeUtc(file).Should().Be(stamp, "nothing changed, so nothing is written");
    }

    /// <summary>
    /// The case the invariant exists for: someone deleted the default's file by hand, or
    /// restored a partial backup. Checking only at setup would leave this silently broken.
    /// </summary>
    [Fact]
    public async Task A_set_whose_default_was_deleted_by_hand_promotes_another_on_next_load()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var other = await repo.CreateAsync("Testing");

        var theDefault = repo.List().Single(l => l.IsDefault);
        File.Delete(Path.Combine(repo.ModlistsDirectory, theDefault.Id + ".json"));

        var lists = await repo.EnsureDefaultAsync(TwoMods);

        lists.Should().ContainSingle().Which.Id.Should().Be(other.Id);
        repo.Get(other.Id)!.IsDefault.Should().BeTrue("the promotion is persisted, not just returned");
    }

    [Fact]
    public async Task The_default_cannot_be_deleted()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        await repo.CreateAsync("Testing");

        var theDefault = repo.List().Single(l => l.IsDefault);

        repo.Delete(theDefault.Id).Should().BeFalse();
        repo.Get(theDefault.Id).Should().NotBeNull();
    }

    /// <summary>The invariant lives in the repository, not the UI — the CLI has no buttons
    /// to grey out.</summary>
    [Fact]
    public async Task The_last_list_standing_cannot_be_deleted_even_if_it_is_not_the_default()
    {
        var repo = Repo();
        var only = await repo.CreateAsync("Only one");

        repo.Delete(only.Id).Should().BeFalse();
        repo.List().Should().ContainSingle();
    }

    [Fact]
    public async Task Deleting_an_ordinary_list_takes_its_snapshots_with_it()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var doomed = await repo.CreateAsync("Testing");

        var snapshots = repo.SnapshotDirectory(doomed.Id);
        Directory.CreateDirectory(snapshots);
        File.WriteAllText(Path.Combine(snapshots, "0001.json"), "{}");

        repo.Delete(doomed.Id).Should().BeTrue();
        repo.Get(doomed.Id).Should().BeNull();
        Directory.Exists(snapshots).Should().BeFalse("orphaned history is just disk nobody can reach");
    }

    [Fact]
    public async Task The_startup_list_is_the_most_recently_used_then_the_default()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var other = await repo.CreateAsync("Testing");

        repo.Selected(repo.List())!.IsDefault.Should().BeTrue("nothing has been used yet");

        await repo.MarkUsedAsync(other);

        repo.Selected(repo.List())!.Id.Should().Be(other.Id);
    }

    [Fact]
    public async Task Marking_a_list_used_does_not_rewrite_its_arrangement()
    {
        var repo = Repo();
        var list = await repo.CreateAsync("Heavily modded", TwoMods());

        await repo.MarkUsedAsync(list);

        var loaded = repo.Get(list.Id)!;
        loaded.LastUsedUtc.Should().NotBeNull();
        loaded.State.Entries.Should().HaveCount(3, "switching a list must not touch what is in it");
    }

    [Fact]
    public void Listing_a_directory_that_does_not_exist_is_the_first_run_case_not_an_error()
    {
        new ModlistRepository(_fs, Path.Combine(_dir, "nope")).List().Should().BeEmpty();
    }

    // --- duplicate ----------------------------------------------------------

    [Fact]
    public async Task Duplicating_copies_the_arrangement_but_not_the_role()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var original = repo.List().Single(l => l.IsDefault);

        var copy = await repo.DuplicateAsync(original, "Experiment");

        copy.Id.Should().NotBe(original.Id);
        copy.Name.Should().Be("Experiment");
        copy.State.Entries.Should().HaveCount(3, "the arrangement is the point of a copy");
        copy.IsDefault.Should().BeFalse("default is a role, not a property of the contents");
        copy.LastUsedUtc.Should().BeNull();

        repo.List().Should().HaveCount(2);
    }

    /// <summary>
    /// A copy has never been applied, so it has no evidence about the game's state.
    /// Inheriting the original's hash would make drift detection confidently wrong.
    /// </summary>
    [Fact]
    public async Task A_copy_does_not_inherit_what_the_original_applied()
    {
        var repo = Repo();
        var original = await repo.CreateAsync("Applied", TwoMods());
        await repo.SaveAsync(original with { LastAppliedHash = "deadbeef" });

        var copy = await repo.DuplicateAsync(repo.Get(original.Id)!, "Copy");

        copy.LastAppliedHash.Should().BeNull();
        copy.LastAppliedUtc.Should().BeNull();
    }

    /// <summary>
    /// Snapshots are the history of how the ORIGINAL got where it is. Attaching that to a
    /// copy would make the copy claim a past it never had.
    /// </summary>
    [Fact]
    public async Task A_copy_starts_with_no_history()
    {
        var repo = Repo();
        var original = await repo.CreateAsync("Original", TwoMods());

        Directory.CreateDirectory(repo.SnapshotDirectory(original.Id));
        File.WriteAllText(Path.Combine(repo.SnapshotDirectory(original.Id), "s.json"), "{}");

        var copy = await repo.DuplicateAsync(original, "Copy");

        Directory.Exists(repo.SnapshotDirectory(copy.Id)).Should().BeFalse();
    }

    // --- the default flag ---------------------------------------------------

    /// <summary>
    /// EnsureDefaultAsync only repairs a broken set — it never reassigns a healthy one —
    /// so without this the first list marked default stayed default for ever, including
    /// whichever one migration happened to pick.
    /// </summary>
    [Fact]
    public async Task The_default_can_be_moved_to_another_list()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var other = await repo.CreateAsync("Testing");

        (await repo.SetDefaultAsync(other.Id)).Should().BeTrue();

        repo.List().Where(l => l.IsDefault).Should().ContainSingle()
            .Which.Id.Should().Be(other.Id);
    }

    [Fact]
    public async Task Moving_the_default_lets_the_old_one_be_deleted()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var wasDefault = repo.List().Single(l => l.IsDefault);
        var other = await repo.CreateAsync("Testing");

        repo.Delete(wasDefault.Id).Should().BeFalse("it is still the default");

        await repo.SetDefaultAsync(other.Id);

        repo.Delete(wasDefault.Id).Should().BeTrue("moving the flag is what unlocks it");
    }

    [Fact]
    public async Task Setting_the_default_to_an_unknown_id_changes_nothing()
    {
        var repo = Repo();
        await repo.EnsureDefaultAsync(TwoMods);
        var before = repo.List().Single(l => l.IsDefault).Id;

        (await repo.SetDefaultAsync("nope")).Should().BeFalse();

        repo.List().Single(l => l.IsDefault).Id.Should().Be(before);
    }
}
