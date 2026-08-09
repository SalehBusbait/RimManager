using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sharing;
using RimManager.Core.Workshop;
using RimManager.Storage.Persistence;

namespace RimManager.Storage.Repositories;

/// <summary>
/// Workspace state that is neither the load order nor per-mod metadata: window
/// layout, update snoozes, and the user's rule overrides, at the app root by default.
/// <para>
/// One repository over three separate files rather than one blob. They have very
/// different write rhythms — layout changes on every splitter drag, rules only when
/// the rule editor is used — and keeping them apart means a layout write can never
/// endanger a user's hand-authored rules. Each is diffable JSON, same as the rest.
/// </para>
/// </summary>
public sealed class WorkspaceStateRepository
{
    private readonly string _layoutFile;
    private readonly string _snoozeFile;
    private readonly string _rulesFile;
    private readonly string _rwListOffersFile;
    private readonly JsonDocumentStore<LayoutState> _layout;
    private readonly JsonDocumentStore<SnoozeSet> _snoozes;
    private readonly JsonDocumentStore<RuleOverrides> _rules;
    private readonly JsonDocumentStore<RwListOfferSeen> _rwListOffers;

    public WorkspaceStateRepository(IFileSystem fs, string? root = null)
    {
        root ??= AppPaths.Root;
        _layoutFile = Path.Combine(root, "layout.json");
        _snoozeFile = Path.Combine(root, "snoozes.json");
        _rulesFile = Path.Combine(root, "rules.json");
        _rwListOffersFile = Path.Combine(root, "rwlistOffers.json");
        _layout = new JsonDocumentStore<LayoutState>(fs);
        _snoozes = new JsonDocumentStore<SnoozeSet>(fs);
        _rules = new JsonDocumentStore<RuleOverrides>(fs);
        _rwListOffers = new JsonDocumentStore<RwListOfferSeen>(fs);
    }

    // --- layout --------------------------------------------------------------

    /// <summary>
    /// The saved layout, or the design's defaults. A missing file is the normal
    /// first-run case, never an error.
    /// </summary>
    public LayoutState LoadLayout() => _layout.Load(_layoutFile) ?? LayoutState.Default;

    public Task SaveLayoutAsync(LayoutState layout, CancellationToken ct = default) =>
        _layout.SaveAsync(_layoutFile, layout, ct: ct);

    // --- update snoozes ------------------------------------------------------

    public SnoozeSet LoadSnoozes() => _snoozes.Load(_snoozeFile) ?? SnoozeSet.Empty;

    public Task SaveSnoozesAsync(SnoozeSet snoozes, CancellationToken ct = default) =>
        _snoozes.SaveAsync(_snoozeFile, snoozes, ct: ct);

    // --- rule overrides ------------------------------------------------------

    public RuleOverrides LoadRuleOverrides() => _rules.Load(_rulesFile) ?? RuleOverrides.Empty;

    public Task SaveRuleOverridesAsync(RuleOverrides overrides, CancellationToken ct = default) =>
        _rules.SaveAsync(_rulesFile, overrides, ct: ct);

    // --- rwlist import offers (NF-10) ----------------------------------------

    public RwListOfferSeen LoadRwListOffers() => _rwListOffers.Load(_rwListOffersFile) ?? RwListOfferSeen.Empty;

    public Task SaveRwListOffersAsync(RwListOfferSeen seen, CancellationToken ct = default) =>
        _rwListOffers.SaveAsync(_rwListOffersFile, seen, ct: ct);
}
