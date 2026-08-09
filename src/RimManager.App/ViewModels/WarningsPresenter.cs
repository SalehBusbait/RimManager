using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using RimManager.Core.Validation;

namespace RimManager.App.ViewModels;

/// <summary>
/// The Warnings dock's six categories, <b>in the order they are always rendered</b>
/// (<c>2a</c>): most-blocking first, informational last. The enum <em>is</em> the
/// order, so a new category cannot be added without deciding where it belongs.
/// <para>
/// Cycles is a category here rather than its own dock tab (design non-negotiable #7):
/// a broken dependency edge is a validation warning, it only ever appears right after
/// a sort, and a tab reading "0" for weeks trains people to ignore the strip.
/// </para>
/// </summary>
public enum WarningGroup
{
    MissingDependencies = 0,
    Incompatibilities = 1,
    Cycles = 2,
    LoadOrder = 3,
    UnsupportedVersion = 4,
    Duplicates = 5,

    /// <summary>N7 · a maintained replacement exists (UseThisInstead). Last: advice,
    /// after everything that is actually wrong.</summary>
    Replaceable = 6,
}

/// <summary>
/// What the severity chips filter on. Deliberately three buckets rather than the
/// domain's two severities: "info" is a real reading category for the user even
/// though nothing in <see cref="ValidationSeverity"/> models it.
/// </summary>
public enum WarningTone
{
    Blocking = 0,
    Warning = 1,
    Info = 2,
}

/// <summary>
/// One row in the Warnings master table — either a group header or an issue. One flat
/// list with a template selector, the same shape the load-order list uses, because
/// grouping that the user can filter across is far simpler flat than nested.
/// </summary>
/// <param name="Issue">
/// Split into three so the packageId can be set in mono without an inline collection:
/// "Requires " + <c>ceteam.cefastrack</c> + " — not installed".
/// </param>
public sealed record WarningEntry(
    WarningGroup Group,
    WarningTone Tone,
    bool IsGroupHeader,
    string Issue,
    string IssueMono,
    string IssueTail,
    string ModName,
    string Category,
    string Fix,
    ModId? Subject = null,
    ModId? Related = null,
    ModId? DeclaredBy = null,
    bool EmphasisIsPackageId = true)
{
    /// <summary>
    /// Whether the emphasised middle run is a packageId, and so should render in mono.
    /// <para>
    /// A packageId is an identifier and reads as one; a mod's NAME is prose and looks
    /// wrong set in a monospace face — "Vanilla Storytellers Expanded - Winston Waves"
    /// most of all. The run is still emphasised either way; only the face changes.
    /// </para>
    /// </summary>
    public bool EmphasisIsMono => EmphasisIsPackageId;

    /// <summary>
    /// The mod this warning BELONGS to — whoever wrote the rule, else the subject.
    /// <para>
    /// This is what puts a glyph on a row, and it is one rule stated once: <b>a row's
    /// warning means something that mod declared is not satisfied.</b> Attribution used
    /// to be subject-and-related, which hung four warnings on RimWorld — a mod that
    /// declares nothing at all — because other mods' rules point at it. A mod is not at
    /// fault for being referred to.
    /// </para>
    /// </summary>
    public ModId? Owner => DeclaredBy ?? Subject;

    /// <summary>Group headers carry their count and a bulk action instead of columns.</summary>
    public int Count { get; init; }

    public string Provenance { get; init; } = string.Empty;

    public string BulkAction { get; init; } = string.Empty;

    /// <summary>Index into the sort's broken edges, or -1. Only cycles have one.</summary>
    public int CycleIndex { get; init; } = -1;

    // Row-shape flags. Computed here rather than with converters in XAML so the
    // severity glyph cannot disagree with the chip that filtered the row in.
    public bool IsIssueRow => !IsGroupHeader;
    public bool IsCycleRow => !IsGroupHeader && Group == WarningGroup.Cycles;
    public bool IsBlockingRow => IsIssueRow && !IsCycleRow && Tone == WarningTone.Blocking;
    public bool IsWarningRow => IsIssueRow && !IsCycleRow && Tone == WarningTone.Warning;
    public bool IsInfoRow => IsIssueRow && !IsCycleRow && Tone == WarningTone.Info;
    public bool HasMono => IssueMono.Length > 0;
    public bool HasTail => IssueTail.Length > 0;

    /// <summary>
    /// The three runs as one sentence, for the tooltip on a row whose issue column has
    /// run out of width. The cell can only ellipsize what it cannot fit; the whole
    /// sentence has to stay reachable, and re-joining it here keeps the split a
    /// rendering detail rather than something the reader has to reassemble.
    /// </summary>
    public string FullIssue => Issue + IssueMono + IssueTail;
    public bool HasBulkAction => BulkAction.Length > 0;
    public bool HasProvenance => Provenance.Length > 0;

    /// <summary>Group-header label tone, for the coloured caps label.</summary>
    public bool IsBlockingGroup => IsGroupHeader && Tone == WarningTone.Blocking;
    public bool IsInfoGroup => IsGroupHeader && Tone == WarningTone.Info;

    /// <summary>
    /// The fix leaves the app (a Workshop search). Marked with the real external-link
    /// geometry rather than a "↗" in the label: Unicode arrows are missing from
    /// several default Linux fonts, where the tofu box is a visible bug (#12).
    /// </summary>
    public bool IsExternalFix => Fix == "Find";

    /// <summary>Not every warning offers one — an empty fix hides the button rather
    /// than rendering a dead one.</summary>
    public bool HasFix => Fix.Length > 0;

    /// <summary>The FIX button's tooltip — what will happen, before the click.</summary>
    public string FixTip => WarningsPresenter.FixTipFor(Fix);
}

/// <summary>
/// One line of the indented edge chain in the detail panel. The dropped edge is struck
/// through in danger rather than removed: the user has to see the edge that was NOT
/// honoured, or "cycle broken" is just an assertion.
/// </summary>
public sealed record WarningChainStep(string Before, string Rule, string After, bool IsDropped, int Indent)
{
    /// <summary>
    /// The step's leading indent in DIPs. Computed here rather than by a converter in
    /// XAML so the chain's staircase is one number in one place.
    /// </summary>
    public double IndentWidth => Indent * 14;
}

/// <summary>
/// A resolution button. Disabled ones still render, with a tip saying what they are
/// waiting on — a hidden action misrepresents the product, a greyed one teaches it.
/// </summary>
public sealed record WarningAction(string Label, string Id, bool IsPrimary, bool IsEnabled, string Tip)
{
    /// <summary>Draws the external-link geometry after the label. See IsExternalFix.</summary>
    public bool IsExternal => Id == "fix" && Label == "Find";
}

/// <summary>A row named by the warning, with its load position. Double-click reveals.</summary>
public sealed record WarningAffectedRow(string Index, string Name, string Note, ModId? Id);

public sealed record WarningDetail(
    string Title,
    string Paragraph,
    ImmutableArray<WarningChainStep> Chain,
    string ChainNote,
    ImmutableArray<WarningAction> Actions,
    ImmutableArray<WarningAffectedRow> Affected)
{
    public static readonly WarningDetail None =
        new(string.Empty, string.Empty, [], string.Empty, [], []);
}

/// <summary>
/// Avalonia-free construction of the Warnings tab (<c>2a</c>): merges the validation
/// report, the last sort's broken edges and the scan's duplicate warnings into the six
/// ordered groups, then applies the severity chip and the search box.
/// <para>
/// Pure because the ordering rule is the whole point of the screen and is invisible to
/// any launch smoke — a group rendered in the wrong place still looks like a working
/// dock.
/// </para>
/// </summary>
public static class WarningsPresenter
{
    public static string LabelFor(WarningGroup group) => group switch
    {
        WarningGroup.MissingDependencies => "MISSING DEPENDENCIES",
        WarningGroup.Incompatibilities => "INCOMPATIBILITIES",
        WarningGroup.Cycles => "CYCLES",
        WarningGroup.LoadOrder => "LOAD ORDER",
        WarningGroup.UnsupportedVersion => "UNSUPPORTED VERSION",
        WarningGroup.Replaceable => "REPLACEMENT AVAILABLE",
        _ => "DUPLICATES",
    };

    /// <summary>
    /// The tone a whole group carries. Missing dependencies and incompatibilities stop
    /// the game from starting; the rest do not — and a replacement existing is advice,
    /// not a defect: the mod may run fine.
    /// </summary>
    public static WarningTone ToneFor(WarningGroup group) => group switch
    {
        WarningGroup.MissingDependencies => WarningTone.Blocking,
        WarningGroup.Incompatibilities => WarningTone.Blocking,
        WarningGroup.Duplicates => WarningTone.Info,
        WarningGroup.Replaceable => WarningTone.Info,
        _ => WarningTone.Warning,
    };

    /// <summary>
    /// A tier override is INFORMATION, not a warning: nothing is broken and the list is
    /// not wrong — a rule simply is not being enforced, by design. Giving it the same
    /// tone as a real order violation would put six rows nobody can act on into the
    /// Warning chip beside two that need fixing.
    /// </summary>
    public static WarningTone ToneForIssue(ValidationIssue issue) =>
        issue.Code is IssueCodes.OrderTierOverride or IssueCodes.ReplacementAvailable
            ? WarningTone.Info
        : issue.Severity == ValidationSeverity.Error ? WarningTone.Blocking
        : WarningTone.Warning;

    private static WarningGroup GroupFor(string code) => code switch
    {
        IssueCodes.MissingDependency => WarningGroup.MissingDependencies,
        IssueCodes.MissingDlc => WarningGroup.MissingDependencies,
        IssueCodes.DependencyInactive => WarningGroup.MissingDependencies,
        IssueCodes.IncompatibleActive => WarningGroup.Incompatibilities,
        IssueCodes.UnsupportedVersion => WarningGroup.UnsupportedVersion,
        IssueCodes.ReplacementAvailable => WarningGroup.Replaceable,
        _ => WarningGroup.LoadOrder,
    };

    private static string CategoryFor(WarningGroup group) => group switch
    {
        WarningGroup.MissingDependencies => "Dependency",
        WarningGroup.Incompatibilities => "Incompatible",
        WarningGroup.Cycles => "Cycle",
        WarningGroup.LoadOrder => "Order · rules",
        WarningGroup.UnsupportedVersion => "Unsupported",
        WarningGroup.Replaceable => "Replaceable",
        _ => "Duplicate",
    };

    /// <summary>
    /// The fix button, which names the action rather than saying "Fix": the user has to
    /// know what will happen before clicking, in a 60px column. All three verbs are
    /// WIRED (the hub's RunWarningFix): Find opens a Workshop search, Activate moves
    /// the inactive dependency into the order, Review reveals the affected rows.
    /// </summary>
    private static string FixFor(string code) => code switch
    {
        IssueCodes.MissingDependency => "Find",
        IssueCodes.MissingDlc => "Find",
        IssueCodes.DependencyInactive => "Activate",
        IssueCodes.IncompatibleActive => "Review",

        // No "Ignore" for an unsupported version: there is no per-warning ignore store
        // to put the choice in, and the honest suppressor that DOES exist — Mlie's
        // known-good list — is a database, not a button. An Ignore that forgot on the
        // next validate would be worse than none.
        IssueCodes.UnsupportedVersion => "",
        IssueCodes.ReplacementAvailable => "Review",

        // NEVER "Move" for a tier override. The rule is not being applied and will not
        // be: moving the mod by hand would fight the sorter, which puts it back on the
        // next sort. Offering the action that undoes itself is worse than offering none.
        IssueCodes.OrderTierOverride => "Review",

        // "Review", not 2a's "Move", for an ordinary order rule: a Move needs the
        // direction as DATA, and ValidationIssue carries it only as prose. Parsing our
        // own sentence back would couple the button to the wording; the group's
        // "Fix all N" (Sort) is the fix that actually exists.
        _ => "Review",
    };

    /// <summary>The fix button's tooltip: what will happen, before the click.</summary>
    public static string FixTipFor(string fix) => fix switch
    {
        "Find" => "Search the Workshop for this packageId — opens in Steam or the browser",
        "Activate" => "Activate the dependency — one undoable edit, like a drag",
        "Review" => "Reveal the affected rows in the lists",
        _ => string.Empty,
    };

    /// <summary>
    /// The bulk action on a group header. Empty where there is no honest bulk fix —
    /// an unsupported version is a fact about the mod, not something to resolve;
    /// an incompatibility is a per-pair judgment about which mod to lose, and no
    /// bulk button can make it; and the cycle graph is RETIRED (v2 S-CYCLE) — the
    /// detail chain with the struck edge is the canonical surface, so "Show graph"
    /// died with it. "Fix all N" runs Sort, which is the machine that fixes order
    /// rules; a per-row hand-move is what the sorter exists to replace.
    /// </summary>
    private static string BulkActionFor(WarningGroup group, int count) => group switch
    {
        WarningGroup.MissingDependencies => "Resolve all…",
        WarningGroup.LoadOrder => $"Fix all {count}",
        _ => string.Empty,
    };

    /// <summary>
    /// Builds every issue, ungrouped and unfiltered. Kept separate from
    /// <see cref="Group"/> so the severity chip counts can be taken from the whole set
    /// while the table shows a filtered subset — a chip that reported the filtered
    /// count would always read the same number as the list beside it.
    /// </summary>
    public static ImmutableArray<WarningEntry> BuildIssues(
        IEnumerable<ValidationIssue> issues,
        SortResult? lastSort,
        IEnumerable<ModWarning> scanWarnings,
        IReadOnlyDictionary<ModId, string> modNames)
    {
        var entries = ImmutableArray.CreateBuilder<WarningEntry>();

        foreach (var issue in issues)
        {
            var group = GroupFor(issue.Code);
            var (text, mono, tail) = SplitMessage(StripSubject(issue.Message, issue.Subject));

            // The validator writes packageIds, which is right for the CLI and the log —
            // they are stable and unambiguous. On screen the user is reading a list of
            // mod NAMES, and "imranfish.xmlextensions" makes them translate. Substituted
            // here rather than in Core so the two audiences each get what suits them.
            var (named, stillAnId) = NameOrId(mono, modNames);
            entries.Add(new WarningEntry(
                group,
                ToneForIssue(issue),
                IsGroupHeader: false,
                text, mono, tail,
                NameOf(issue.Subject, modNames),
                CategoryFor(group),
                FixFor(issue.Code),
                issue.Subject,
                issue.Related,
                issue.DeclaredBy,
                stillAnId) { IssueMono = named });
        }

        if (lastSort is { BrokenEdges.IsDefaultOrEmpty: false })
        {
            for (var i = 0; i < lastSort.BrokenEdges.Length; i++)
            {
                var broken = lastSort.BrokenEdges[i];
                var edge = $"{broken.Edge.Before.Display} → {broken.Edge.After.Display}";
                entries.Add(new WarningEntry(
                    WarningGroup.Cycles,
                    WarningTone.Warning,
                    IsGroupHeader: false,
                    $"Cycle of {broken.Cycle.Length} broken — dropped edge ",
                    edge,
                    string.Empty,
                    NameOf(broken.Cycle.Length > 0 ? broken.Cycle[0] : null, modNames)
                        + (broken.Cycle.Length > 1 ? $" +{broken.Cycle.Length - 1}" : string.Empty),
                    CategoryFor(WarningGroup.Cycles),
                    "Review",
                    broken.Cycle.Length > 0 ? broken.Cycle[0] : null,
                    broken.Edge.After) { CycleIndex = i });
            }
        }

        foreach (var warning in scanWarnings.Where(w => w.Code == "duplicate.packageId"))
        {
            entries.Add(new WarningEntry(
                WarningGroup.Duplicates,
                WarningTone.Info,
                IsGroupHeader: false,
                "2 mods declare the same packageId — the later folder wins",
                string.Empty,
                string.Empty,
                NameOf(warning.Subject, modNames),
                CategoryFor(WarningGroup.Duplicates),
                "Compare",
                warning.Subject));
        }

        return entries.ToImmutable();
    }

    /// <summary>
    /// Applies the chip and the search, then emits the flat list with a header before
    /// each non-empty group, always in <see cref="WarningGroup"/> order.
    /// <para>
    /// The chips "filter without regrouping" (<c>2a</c>): a filtered view keeps the
    /// same headings in the same places, so the shape of the list does not change
    /// under the user as they narrow it.
    /// </para>
    /// </summary>
    public static ImmutableArray<WarningEntry> Group(
        ImmutableArray<WarningEntry> issues,
        WarningTone? tone,
        string? search,
        string cyclesProvenance = "")
    {
        var query = search?.Trim();
        var matching = issues.Where(e =>
            (tone is null || e.Tone == tone) &&
            (string.IsNullOrEmpty(query) || Matches(e, query)));

        var rows = ImmutableArray.CreateBuilder<WarningEntry>();
        foreach (var group in Enum.GetValues<WarningGroup>().OrderBy(g => (int)g))
        {
            var inGroup = matching.Where(e => e.Group == group).ToArray();
            if (inGroup.Length == 0) continue;

            rows.Add(new WarningEntry(
                group, ToneFor(group), IsGroupHeader: true,
                LabelFor(group), string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty)
            {
                Count = inGroup.Length,
                Provenance = group == WarningGroup.Cycles
                    ? cyclesProvenance
                    : ToneFor(group) == WarningTone.Blocking ? "blocking"
                    : ToneFor(group) == WarningTone.Info ? "info"
                    : string.Empty,
                BulkAction = BulkActionFor(group, inGroup.Length),
            });

            rows.AddRange(inGroup);
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// The detail panel for one selected warning (<c>2a</c>): a plain-language
    /// paragraph, the cycle as an indented edge chain with the dropped edge struck,
    /// why it was dropped, the resolution buttons, and the affected rows.
    /// <para>
    /// This is "the reason Cycles is a category here and not its own tab" — the panel
    /// carries everything the old Cycles tab did, next to every other warning.
    /// </para>
    /// </summary>
    /// <param name="positionOf">
    /// The mod's 1-based position in the active load order, or null when it is not
    /// active. Null and 0 stay distinct: "not in the list" is not "first".
    /// </param>
    public static WarningDetail BuildDetail(
        WarningEntry? entry,
        SortResult? lastSort,
        Func<ModId, int?> positionOf,
        IReadOnlyDictionary<ModId, string> modNames)
    {
        if (entry is null || entry.IsGroupHeader) return WarningDetail.None;

        return entry.Group switch
        {
            WarningGroup.Cycles => CycleDetail(entry, lastSort, positionOf, modNames),
            _ => PlainDetail(entry, positionOf, modNames),
        };
    }

    private static WarningDetail CycleDetail(
        WarningEntry entry, SortResult? lastSort,
        Func<ModId, int?> positionOf, IReadOnlyDictionary<ModId, string> modNames)
    {
        if (lastSort is null || entry.CycleIndex < 0
            || lastSort.BrokenEdges.IsDefaultOrEmpty
            || entry.CycleIndex >= lastSort.BrokenEdges.Length)
        {
            return PlainDetail(entry, positionOf, modNames);
        }

        var broken = lastSort.BrokenEdges[entry.CycleIndex];
        var cycle = broken.Cycle;

        var chain = ImmutableArray.CreateBuilder<WarningChainStep>(cycle.Length);
        for (var i = 0; i < cycle.Length; i++)
        {
            var before = cycle[i];
            var after = cycle[(i + 1) % cycle.Length];
            var dropped = before == broken.Edge.Before && after == broken.Edge.After;
            chain.Add(new WarningChainStep(
                NameOf(before, modNames), "after", NameOf(after, modNames), dropped, i));
        }

        var affected = ImmutableArray.CreateBuilder<WarningAffectedRow>(cycle.Length);
        foreach (var id in cycle) affected.Add(AffectedRow(id, positionOf, modNames));

        return new WarningDetail(
            "Dependency cycle broken to finish the sort",
            $"{cycle.Length} mods each declare they must load after another in the group. "
            + "No order satisfies all of them, so the sort dropped the lowest-confidence edge "
            + "and continued. Nothing is wrong with your list — but the dropped rule is not "
            + "being honoured.",
            chain.ToImmutable(),
            DropReason(broken),
            [
                // Both WIRED (the hub's RunWarningAction): accept pins the sorter's
                // choice on the MODLIST via EdgeSuppressions — the store R1b built and
                // this button spent five phases claiming to wait for. "Drop a different
                // edge…" stays absent: without the retired graph it needs pick-an-edge-
                // from-the-chain, an interaction the chain has not grown yet. "Show
                // graph" died with the graph (v2 S-CYCLE).
                new WarningAction("Accept dropped edge", "accept", true, true,
                    "Pin this choice on the modlist — every later sort drops the same "
                    + "edge instead of re-deciding"),
                new WarningAction("Edit rule", "edit-rule", false, true,
                    "Open the rule editor — switch the conflicting rule off, or add "
                    + "your own"),
            ],
            affected.ToImmutable());
    }

    /// <summary>
    /// Why the sorter picked this edge. Deliberately no vote count: RimSort's
    /// Community-Rules-Database ships comments, not votes, and the mockup's
    /// "3 votes" is data we do not have. The reason is the same without it.
    /// </summary>
    private static string DropReason(BrokenEdge broken) => broken.Edge.Provenance.Source switch
    {
        RuleSource.Community =>
            "Dropped because it comes from a community rule, while the others in the cycle "
            + "are declared in About.xml.",
        RuleSource.User =>
            "Dropped because it is one of your own rules — the sorter breaks the edge you "
            + "can change, not one you cannot.",
        _ =>
            "Dropped because it was the lowest-confidence edge in the cycle; every edge here "
            + "is declared in About.xml, so the choice is made deterministically by id.",
    };

    private static WarningDetail PlainDetail(
        WarningEntry entry, Func<ModId, int?> positionOf, IReadOnlyDictionary<ModId, string> modNames)
    {
        var affected = ImmutableArray.CreateBuilder<WarningAffectedRow>(2);
        if (entry.Subject is { } subject) affected.Add(AffectedRow(subject, positionOf, modNames));
        if (entry.Related is { } related && related != entry.Subject)
            affected.Add(AffectedRow(related, positionOf, modNames));

        var text = entry.Issue + entry.IssueMono + entry.IssueTail;

        // The one fix, WIRED, and only when the row has one. "Ignore this warning" is
        // GONE rather than greyed: there is no per-warning ignore store, and a button
        // that has waited five phases for one is not a promise — it is furniture.
        ImmutableArray<WarningAction> actions = entry.HasFix
            ? [new WarningAction(entry.Fix, "fix", true, true, FixTipFor(entry.Fix))]
            : [];

        return new WarningDetail(
            TitleFor(entry.Group),
            $"{text} {ExplanationFor(entry.Group)}",
            [],
            string.Empty,
            actions,
            affected.ToImmutable());
    }

    /// <summary>
    /// What the warning means for the player, in one sentence. The row already says
    /// what is wrong; the panel has to say what happens if it is left alone, which is
    /// the question the row cannot answer in its width.
    /// </summary>
    private static string ExplanationFor(WarningGroup group) => group switch
    {
        WarningGroup.MissingDependencies =>
            "The game will still start, but the dependent mod's content will not load and "
            + "usually throws errors on the first save you open.",
        WarningGroup.Incompatibilities =>
            "The authors declared these two must not run together. Expect broken defs or a "
            + "hard error on load; deactivate one of them.",
        WarningGroup.LoadOrder =>
            "The list works, but a rule that the authors or the community wrote is not being "
            + "honoured, which usually shows up as one mod's patch silently losing.",
        WarningGroup.UnsupportedVersion =>
            "The mod may still work — About.xml is a claim, not a test — but nothing has "
            + "been checked against your game version.",
        WarningGroup.Duplicates =>
            "Only the higher-precedence folder loads. This is normal when you have both a "
            + "Workshop copy and a local edit, and a problem when you did not mean to.",
        _ => string.Empty,
    };

    private static string TitleFor(WarningGroup group) => group switch
    {
        WarningGroup.MissingDependencies => "A dependency is missing or inactive",
        WarningGroup.Incompatibilities => "Two active mods declare each other incompatible",
        WarningGroup.LoadOrder => "A load-order rule is not being honoured",
        WarningGroup.UnsupportedVersion => "This mod does not declare support for your game version",
        WarningGroup.Duplicates => "Two folders declare the same packageId",
        _ => "Warning",
    };

    private static WarningAffectedRow AffectedRow(
        ModId id, Func<ModId, int?> positionOf, IReadOnlyDictionary<ModId, string> modNames)
    {
        var position = positionOf(id);
        return new WarningAffectedRow(
            position is { } p ? p.ToString() : "—",
            NameOf(id, modNames),
            position is null ? "inactive" : string.Empty,
            id);
    }

    private static bool Matches(WarningEntry entry, string query) =>
        entry.Issue.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.IssueMono.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.IssueTail.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.ModName.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The emphasised run as a NAME when the id belongs to an installed mod, and as the
    /// id otherwise — a missing dependency has no name to show, and inventing one would
    /// be worse than the identifier.
    /// </summary>
    private static (string Text, bool IsPackageId) NameOrId(
        string mono, IReadOnlyDictionary<ModId, string> names)
    {
        if (mono.Length == 0) return (mono, true);
        if (!ModId.TryFrom(mono, out var id)) return (mono, true);
        if (!names.TryGetValue(id, out var name) || name.Length == 0) return (mono, true);

        return (name, false);
    }

    private static string NameOf(ModId? id, IReadOnlyDictionary<ModId, string> names) =>
        id is { } key && names.TryGetValue(key, out var name) ? name : id?.Display ?? string.Empty;

    /// <summary>
    /// Pulls a quoted packageId out of a validator message so it can render in mono,
    /// as <c>2a</c> shows. The validator writes human sentences with the id in single
    /// quotes; anything that does not match that shape is left whole rather than
    /// guessed at.
    /// </summary>
    /// <summary>
    /// Drops a leading "'subject' " from a validator message. The MOD column already
    /// names the subject, so leading with its packageId spends the widest column in
    /// the table on a repeat — 2a's issue text starts with the verb.
    /// </summary>
    /// <summary>
    /// The warning as it must read on one particular mod's row (N2).
    /// <para>
    /// The stored text has its subject elided, because the dock's MOD column already
    /// names it and repeating a packageId there spends the widest column on a repeat.
    /// That is right in the table and <b>wrong on the other mod's row</b>: measured on
    /// a real install, XML Extensions' row read "Should load before
    /// <c>imranfish.xmlextensions</c>…" — which is its own packageId — and Achtung's
    /// read "Is incompatible with <c>brrainz.achtung</c>", which is itself. Both are
    /// self-referential nonsense, and both build, test and render perfectly.
    /// </para>
    /// <para>
    /// So the subject is put back whenever the row is not the subject. Un-capitalising
    /// the first letter is the exact inverse of what <see cref="StripSubject"/> did, and
    /// safe because these sentences always resume with a verb.
    /// </para>
    /// </summary>
    public static string MessageFor(WarningEntry entry, ModId row)
    {
        var text = entry.FullIssue;
        if (entry.Subject is not { } subject || subject == row || text.Length == 0) return text;

        var name = entry.ModName.Length > 0 ? entry.ModName : subject.Display;
        return $"{name} {char.ToLowerInvariant(text[0])}{text[1..]}";
    }

    public static string StripSubject(string message, ModId? subject)
    {
        if (subject is not { } id) return message;

        var prefix = $"'{id.Display}' ";
        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return message;

        var rest = message[prefix.Length..];
        return rest.Length == 0 ? rest : char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    public static (string Head, string Mono, string Tail) SplitMessage(string message)
    {
        var open = message.IndexOf('\'');
        if (open < 0) return (message, string.Empty, string.Empty);

        var close = message.IndexOf('\'', open + 1);
        if (close < 0) return (message, string.Empty, string.Empty);

        return (message[..open], message[(open + 1)..close], message[(close + 1)..]);
    }
}
