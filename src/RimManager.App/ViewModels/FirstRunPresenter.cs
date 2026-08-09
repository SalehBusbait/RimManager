using System.Collections.Generic;
using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.App.ViewModels;

/// <summary>The four steps of first run (<c>2j</c>), in order.</summary>
public enum FirstRunStep
{
    Welcome = 0,
    Paths = 1,
    Modlist = 2,
    Rules = 3,
}

/// <summary>How one node of the progress chain renders.</summary>
public enum ChainNodeState
{
    /// <summary>Behind us: success fill and a ✓.</summary>
    Done,

    /// <summary>Accent fill, its number, and a 600-weight label.</summary>
    Current,

    /// <summary>Outline only, at <c>text.tertiary</c>.</summary>
    Upcoming,
}

/// <summary>One separator the wizard offers to create for the imported order.</summary>
public sealed record ProposedGroup(string Name, int Count, int PaletteIndex);

/// <summary>
/// One node of the 4-step progress chain. A row of these rather than eight bound bools
/// on the wizard: the chain is four of one thing, so it gets one template.
/// </summary>
public sealed partial class ChainNodeViewModel(int number, string title, bool isLast)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Number { get; } = number;
    public string Title { get; } = title;

    /// <summary>The 18px rule joins nodes, so the last one has nothing to join to.</summary>
    public bool HasRule { get; } = !isLast;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(
        nameof(IsDone), nameof(IsCurrent), nameof(IsUpcoming))]
    private ChainNodeState _state;

    public bool IsDone => State == ChainNodeState.Done;
    public bool IsCurrent => State == ChainNodeState.Current;
    public bool IsUpcoming => State == ChainNodeState.Upcoming;
}

/// <summary>
/// Avalonia-free logic behind the first-run wizard (<c>2j</c>): the progress chain, the
/// per-step wording, and the separator groups step 3 proposes.
/// <para>
/// The groups are <b>derived from a real sort</b> rather than guessed here, because
/// step 3 puts counts on screen and offers to act on them. Naming them from the same
/// tier mapping the Auto-layout command uses is what stops the wizard proposing groups
/// that the app would then build differently.
/// </para>
/// </summary>
public static class FirstRunPresenter
{
    // "Instance" died with instances (T6, S-FIRSTRUN): step 3 names the first
    // MODLIST, and the chain says what the step asks for.
    public static readonly ImmutableArray<string> StepTitles =
        ["Welcome", "Paths", "Modlist", "Rules"];

    public static ChainNodeState NodeState(int node, FirstRunStep current) =>
        node < (int)current ? ChainNodeState.Done
        : node == (int)current ? ChainNodeState.Current
        : ChainNodeState.Upcoming;

    /// <summary>
    /// The footer's left-hand line. Each step reassures about the thing that step just
    /// asked for, rather than repeating one sentence four times.
    /// </summary>
    public static string FooterHint(FirstRunStep step) => step switch
    {
        FirstRunStep.Welcome => "Step 1 of 4 · about 3 minutes",
        // D7 · ▸ , not → . Four other menu paths in the app use ▸, and this line is read
        // by every new user on step 2. (The → in "Get started →" below stays: that is a
        // direction of travel, not a menu path.)
        FirstRunStep.Paths => "You can change all of this later in Settings ▸ Paths.",
        FirstRunStep.Modlist => "Still nothing written to your game folder.",
        _ => "All of this lives in Settings afterwards.",
    };

    public static string PrimaryLabel(FirstRunStep step) => step switch
    {
        FirstRunStep.Welcome => "Get started →",
        FirstRunStep.Rules => "Open RimManager",
        _ => "Continue",
    };

    /// <summary>
    /// The tier a mod sorted into, as the separator name Auto-layout would give it.
    /// One mapping, shared, so the proposal and the thing built from it agree.
    /// </summary>
    public static string TierGroupName(Tier tier) => tier switch
    {
        // NOT "Load first": since Tier.Top was corrected to mean "first among mods",
        // the group that loads first is "Load before Core". These are the frameworks a
        // loadTop rule marks, sitting between the DLC and everything else.
        Tier.Top => "Load early",
        // Named for what the group DOES, matching "Load first" / "Load last". It was
        // "Harmony" when Harmony was the only mod that could be here; it now holds every
        // pre-patcher that declares it loads before the base game, so naming it after one
        // member would mislabel a separator the user keeps.
        Tier.PreCore => "Load before Core",
        Tier.Core or Tier.Dlc => "Core & DLC",
        Tier.Bottom => "Load last",
        _ => "Mods",
    };

    /// <summary>A stable hue per group, so the wizard and the built separators match.</summary>
    public static int PaletteIndexFor(string groupName) => groupName switch
    {
        "Core & DLC" => 0,      // blue
        "Load before Core" => 4,  // violet
        "Load early" => 1,      // green
        "Load last" => 3,       // red
        _ => 5,                 // slate
    };

    /// <summary>
    /// The groups step 3 proposes, in load order, with the number of mods each would
    /// hold. Contiguous runs of one tier become one group — the same coalescing
    /// Auto-layout does, because a second run of the same tier is not a second group.
    /// </summary>
    public static ImmutableArray<ProposedGroup> ProposedGroups(SortResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var groups = ImmutableArray.CreateBuilder<ProposedGroup>();
        string? current = null;
        var count = 0;

        foreach (var id in result.Order)
        {
            var name = TierGroupName(result.Tiers.GetValueOrDefault(id, Tier.Normal));
            if (name != current)
            {
                if (current is not null) groups.Add(new ProposedGroup(current, count, PaletteIndexFor(current)));
                current = name;
                count = 0;
            }

            count++;
        }

        if (current is not null) groups.Add(new ProposedGroup(current, count, PaletteIndexFor(current)));
        return groups.ToImmutable();
    }

    /// <summary>
    /// "4 DLC · 324 Workshop · 14 local" — only the sources actually present, because a
    /// row reading "0 Workshop" on a GOG install is noise about something that will
    /// never apply.
    /// </summary>
    public static string SourcesLine(IReadOnlyDictionary<ModSource, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var parts = new List<string>();
        void Add(ModSource source, string label)
        {
            if (counts.TryGetValue(source, out var n) && n > 0) parts.Add($"{n} {label}");
        }

        Add(ModSource.Dlc, "DLC");
        Add(ModSource.Workshop, "Workshop");
        Add(ModSource.Local, "local");
        Add(ModSource.Git, "git");

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }
}
