using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;

namespace RimManager.App.ViewModels;

/// <summary>One mod in the editor's left-hand list, with how many rules touch it.</summary>
public sealed partial class RuleModViewModel(Mod mod, int active, int disabled) : ObservableObject
{
    public Mod Mod { get; } = mod;
    public ModId PackageId => Mod.PackageId;
    public string Name => Mod.Name;
    public string PackageIdText => Mod.PackageId.Display;

    [ObservableProperty] private string _count = RuleEditorPresenter.CountLabel(active, disabled);

    /// <summary>Mods with no rules are still listed — you have to be able to add the first
    /// one — but they say so rather than showing a bare zero.</summary>
    public bool HasRules => active + disabled > 0;
}

/// <summary>
/// The rule editor (<c>2i</c>-5). <b>Non-modal</b>, deliberately: it is a reference you keep
/// open beside the load order while you work out why something sits where it does, and a
/// modal would put the list you are reasoning about behind it.
/// </summary>
public sealed partial class RuleEditorViewModel : ObservableObject
{
    private readonly LoadOrderRules _merged;
    private readonly IReadOnlyDictionary<ModId, Mod> _installed;
    private readonly Func<RuleOverrides, Task> _save;

    public RuleEditorViewModel(
        IReadOnlyList<Mod> mods,
        LoadOrderRules merged,
        RuleOverrides overrides,
        Func<RuleOverrides, Task> save)
    {
        _merged = merged;
        _installed = mods.ToDictionary(m => m.PackageId);
        _save = save;
        _overrides = overrides;

        foreach (var mod in mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rows = RuleEditorPresenter.RowsFor(mod.PackageId, merged, overrides, _installed);
            Mods.Add(new RuleModViewModel(mod, rows.Count(r => !r.IsDisabled), rows.Count(r => r.IsDisabled)));
        }

        SelectedMod = Mods.FirstOrDefault(m => m.HasRules) ?? Mods.FirstOrDefault();
    }

    public ObservableCollection<RuleModViewModel> Mods { get; } = [];
    public ObservableCollection<RuleRow> Rules { get; } = [];

    /// <summary>Every installed mod, for the "add a rule" picker's other end.</summary>
    public ObservableCollection<RuleModViewModel> Candidates { get; } = [];

    public static string PrecedenceNote => RuleEditorPresenter.PrecedenceNote;

    [ObservableProperty] private RuleOverrides _overrides;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedTitle))]
    private RuleModViewModel? _selectedMod;

    public bool HasSelection => SelectedMod is not null;
    public string SelectedTitle => SelectedMod?.Name ?? "Select a mod";

    /// <summary>Filters the left-hand list. A 400-mod list needs it.</summary>
    [ObservableProperty] private string _filter = string.Empty;

    /// <summary>Show only mods that actually carry rules — the usual reason to be here.</summary>
    [ObservableProperty] private bool _withRulesOnly = true;

    [ObservableProperty] private string _status = string.Empty;

    /// <summary>The other end of a rule being added, and which way round it reads.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRuleCommand))]
    private RuleModViewModel? _newRuleOther;

    [ObservableProperty] private bool _newRuleLoadsAfter = true;

    partial void OnSelectedModChanged(RuleModViewModel? value) => RefreshRules();
    partial void OnFilterChanged(string value) => RefreshCandidates();
    partial void OnWithRulesOnlyChanged(bool value) => RefreshCandidates();

    /// <summary>The left-hand list after the filter and the toggle.</summary>
    public ObservableCollection<RuleModViewModel> VisibleMods { get; } = [];

    /// <summary>Call once after construction; the view binds to VisibleMods.</summary>
    public void Initialise()
    {
        RefreshCandidates();
        RefreshRules();
    }

    private void RefreshCandidates()
    {
        var query = Filter.Trim();

        VisibleMods.Clear();
        foreach (var mod in Mods)
        {
            if (WithRulesOnly && !mod.HasRules) continue;
            if (query.Length > 0
                && !mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !mod.PackageIdText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisibleMods.Add(mod);
        }

        // The picker is never filtered: the mod you want to relate this one to is very
        // often the one that does NOT already have rules.
        if (Candidates.Count == 0)
        {
            foreach (var mod in Mods) Candidates.Add(mod);
        }
    }

    private void RefreshRules()
    {
        Rules.Clear();
        if (SelectedMod is not { } mod) return;

        foreach (var row in RuleEditorPresenter.RowsFor(mod.PackageId, _merged, Overrides, _installed))
        {
            Rules.Add(row);
        }
    }

    /// <summary>
    /// Switches a community rule off. Recorded as a disabled identity, never a deletion —
    /// so the row stays visible and a database resync cannot quietly restore it.
    /// </summary>
    [RelayCommand]
    private async Task DisableRule(RuleRow? row)
    {
        if (row is null || SelectedMod is not { } mod || !row.CanDisable) return;

        var (before, after) = Pair(mod.PackageId, row);
        Overrides = row.IsDisabled
            ? Overrides.Enable(before, after)
            : Overrides.Disable(before, after, "switched off in the rule editor");

        await Persist(row.IsDisabled
            ? $"Re-enabled the rule with {row.OtherName}."
            : $"Switched off the rule with {row.OtherName}. It is remembered, not deleted.");
    }

    [RelayCommand]
    private async Task DeleteRule(RuleRow? row)
    {
        if (row is null || SelectedMod is not { } mod || !row.CanDelete) return;

        var (before, after) = Pair(mod.PackageId, row);
        Overrides = Overrides.WithoutUserRule(before, after);
        await Persist($"Removed your rule with {row.OtherName}.");
    }

    /// <summary>Adds a rule of the user's own — the highest-precedence source. Disabled
    /// until a counterpart is picked (UI audit — it used to be always-enabled and
    /// silently no-op with the ComboBox empty, which is a click that does nothing
    /// with zero explanation).</summary>
    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private async Task AddRule()
    {
        if (SelectedMod is not { } mod || NewRuleOther is not { } other) return;
        if (other.PackageId == mod.PackageId)
        {
            Status = "A mod cannot be ordered against itself.";
            return;
        }

        var (before, after) = NewRuleLoadsAfter
            ? (other.PackageId, mod.PackageId)
            : (mod.PackageId, other.PackageId);

        Overrides = Overrides.WithUserRule(new UserRule(before, after, "added in the rule editor"));
        await Persist($"Added: {mod.Name} loads {(NewRuleLoadsAfter ? "after" : "before")} {other.Name}.");
    }

    private bool CanAddRule() => NewRuleOther is not null;

    /// <summary>
    /// From the selected mod's point of view back to the graph's (before, after) pair.
    /// Getting this backwards would disable the opposite rule, which is the kind of error
    /// that shows up three sorts later.
    /// </summary>
    private static (ModId Before, ModId After) Pair(ModId subject, RuleRow row) =>
        row.Direction == RuleDirection.After
            ? (row.Other, subject)
            : (subject, row.Other);

    private async Task Persist(string status)
    {
        await _save(Overrides);
        RefreshRules();
        RefreshCounts();
        Status = status;
    }

    private void RefreshCounts()
    {
        foreach (var mod in Mods)
        {
            var rows = RuleEditorPresenter.RowsFor(mod.PackageId, _merged, Overrides, _installed);
            mod.Count = RuleEditorPresenter.CountLabel(
                rows.Count(r => !r.IsDisabled), rows.Count(r => r.IsDisabled));
        }
    }
}
