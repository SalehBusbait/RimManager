using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row of Settings ▸ Modlists. Replaces <c>InstanceRowViewModel</c>; the table keeps
/// its geometry and its columns change meaning — MODS rather than ACTIVE, and a SETTINGS
/// column where an instance reported disk size, because what a modlist holds that is worth
/// counting is its captured mod settings.
/// </summary>
public sealed partial class ModlistRowViewModel : ObservableObject
{
    public ModlistRowViewModel(
        Modlist modlist, int snapshots, int settingsFiles, bool isCurrent, DateTimeOffset now)
    {
        Modlist = modlist;
        Id = modlist.Id;
        _name = modlist.Name;
        PaletteIndex = Palette.Normalize(modlist.PaletteIndex);
        Snapshots = snapshots;
        SettingsFiles = settingsFiles;
        IsCurrent = isCurrent;
        IsDefault = modlist.IsDefault;
        CapturesModSettings = modlist.CapturesModSettings;
        LastUsed = ModlistsPresenter.LastUsed(modlist.LastUsedUtc, now);

        Mods = modlist.State.Entries.Count(e => e.Kind == ModlistEntryKind.Mod);
        Separators = modlist.State.Entries.Count(e => e.Kind == ModlistEntryKind.Separator);

        // A list that captures nothing shows a dash rather than 0. Zero is a measurement
        // and this is an absence — the same distinction the git commits-behind column
        // makes between null and nought.
        Settings = modlist.CapturesModSettings ? settingsFiles.ToString() : "—";
    }

    public Modlist Modlist { get; }
    public string Id { get; }

    [ObservableProperty] private string _name;

    public int PaletteIndex { get; }
    public int Snapshots { get; }
    public int SettingsFiles { get; }
    public int Mods { get; }
    public int Separators { get; }
    public string Settings { get; }
    public string LastUsed { get; }

    /// <summary>Whether this is the list the window currently has open.</summary>
    public bool IsCurrent { get; }

    /// <summary>The undeletable one. Shown, because "why can I not delete this" needs an
    /// answer visible before the button is reached for.</summary>
    public bool IsDefault { get; }

    public bool CapturesModSettings { get; }

    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;
}
