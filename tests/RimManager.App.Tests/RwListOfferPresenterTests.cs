using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Sharing;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>The rwlist import offer's decisions (NF-10, S-RWLIST).</summary>
public sealed class RwListOfferPresenterTests
{
    private static Mod Item(string id, string name, ModSource source = ModSource.Workshop,
        string? workshopId = null, ContentFlags content = ContentFlags.RwList) => new()
    {
        PackageId = ModId.From(id),
        Name = name,
        Source = source,
        RootPath = "/ws/" + id,
        PublishedFileId = workshopId,
        Content = content,
    };

    [Fact]
    public void Offers_the_first_unseen_list_item_by_name()
    {
        var offer = RwListOfferPresenter.NextUnseen(
            [Item("b.list", "Beta List"), Item("a.list", "Alpha List")],
            RwListOfferSeen.Empty);

        offer!.ModName.Should().Be("Alpha List", "name order keeps the sequence stable across rescans");
    }

    [Fact]
    public void A_seen_item_is_never_offered_again_and_the_next_takes_the_band()
    {
        var seen = RwListOfferSeen.Empty.MarkSeen("a.list");

        var offer = RwListOfferPresenter.NextUnseen(
            [Item("a.list", "Alpha List"), Item("b.list", "Beta List")], seen);

        offer!.ModName.Should().Be("Beta List");
    }

    /// <summary>
    /// T7 decision 2: the automatic offer is Workshop-only — a local list item is
    /// almost always the user's own export. (The context menu still serves any source.)
    /// </summary>
    [Fact]
    public void Local_list_items_are_not_offered()
    {
        RwListOfferPresenter.NextUnseen(
                [Item("a.list", "Alpha List", ModSource.Local)], RwListOfferSeen.Empty)
            .Should().BeNull();
    }

    [Fact]
    public void Ordinary_mods_are_not_offered()
    {
        RwListOfferPresenter.NextUnseen(
                [Item("a.mod", "A Mod", content: ContentFlags.Defs)], RwListOfferSeen.Empty)
            .Should().BeNull();
    }

    /// <summary>The seen key survives resubscribing (Workshop id), and an item without
    /// one still has a stable key (packageId).</summary>
    [Fact]
    public void The_seen_key_prefers_the_workshop_id()
    {
        RwListOfferPresenter.SeenKeyFor(Item("a.list", "A", workshopId: "12345"))
            .Should().Be("12345");
        RwListOfferPresenter.SeenKeyFor(Item("a.list", "A"))
            .Should().Be("a.list");
    }

    [Fact]
    public void The_strip_names_the_item()
    {
        var offer = RwListOfferPresenter.NextUnseen(
            [Item("a.list", "Alpha List")], RwListOfferSeen.Empty);

        RwListOfferPresenter.StripHeadline(offer!)
            .Should().Contain("Alpha List").And.Contain("looks like a mod list");
    }

    [Fact]
    public void The_dialog_facts_count_mods_and_separators()
    {
        var list = new RwList
        {
            Name = "Combat Overhaul",
            Author = "someone",
            GameVersion = "1.6",
            Entries =
            [
                RwEntry.Separator("s", "CORE"),
                RwEntry.Mod("a.b", "A", RwSource.Workshop),
                RwEntry.Mod("c.d", "C", RwSource.Workshop),
            ],
        };
        var offer = new RwListOffer(ModId.From("x.y"), "1", "Item", "/ws/1");

        var vm = new RwListOfferViewModel(offer, "list.rwlist", list, parseError: null);

        vm.Facts.Should().Be("list.rwlist · 2 mods · 1 separators · by someone · for 1.6");
        vm.CanImport.Should().BeTrue();
    }

    /// <summary>
    /// A checksum mismatch is stated IN the dialog, and does not disable the import.
    /// <para>
    /// It used to be written to the status bar one line before this modal opened —
    /// centred over the window, which is what the user is reading — so the app detected
    /// edited or damaged content, said so where it could not be seen, and then asked for
    /// consent as though nothing were wrong. The import stays enabled on purpose: the
    /// list parsed, and a hand-edited .rwlist is a legitimate thing to have. The point is
    /// that the fact is in front of the user at the moment they decide.
    /// </para>
    /// </summary>
    [Fact]
    public void A_checksum_mismatch_is_stated_in_the_dialog_without_blocking_import()
    {
        var list = new RwList
        {
            Name = "Edited list",
            Entries = [RwEntry.Mod("a.b", "A", RwSource.Workshop)],
        };
        var offer = new RwListOffer(ModId.From("x.y"), "1", "Item", "/ws/1");

        var vm = new RwListOfferViewModel(
            offer, "list.rwlist", list, parseError: null, checksumValid: false);

        vm.ChecksumMismatch.Should().BeTrue("the dialog is where consent is asked for");
        vm.ChecksumWarning.Should().Contain("edited or damaged");
        vm.CanImport.Should().BeTrue(
            "the list parsed; a hand-edited .rwlist is legitimate, so this informs rather "
            + "than blocks");
    }

    /// <summary>
    /// A payload that failed to PARSE shows its parse error and no checksum complaint —
    /// two alarms for one broken file is noise, and the checksum of something that never
    /// parsed says nothing useful.
    /// </summary>
    [Fact]
    public void A_parse_failure_does_not_also_raise_the_checksum_warning()
    {
        var offer = new RwListOffer(ModId.From("x.y"), "1", "Item", "/ws/1");

        var vm = new RwListOfferViewModel(
            offer, "list.rwlist", null, "not json", checksumValid: false);

        vm.HasError.Should().BeTrue();
        vm.ChecksumMismatch.Should().BeFalse();
    }

    [Fact]
    public void A_payload_that_does_not_parse_disables_import_and_says_why()
    {
        var offer = new RwListOffer(ModId.From("x.y"), "1", "Item", "/ws/1");

        var vm = new RwListOfferViewModel(offer, "list.rwlist", null, "not json");

        vm.CanImport.Should().BeFalse();
        vm.HasError.Should().BeTrue();
        vm.Accepted.Should().BeFalse("a dismissed dialog can never read as an import");
    }
}
