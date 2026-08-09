using RimManager.Core.Domain;
using RimManager.Core.Scanning;

namespace RimManager.Core.Locators;

public enum InstallKind
{
    Steam,
    Gog,
    Manual,
}

/// <summary>
/// A discovered RimWorld installation: the paths a scan needs. <see cref="WorkshopDir"/>
/// is null for non-Steam installs.
/// </summary>
public sealed record InstallLayout
{
    public required string GameDir { get; init; }
    public required InstallKind Kind { get; init; }

    /// <summary><c>&lt;GameDir&gt;/Data</c> — Core + official DLCs.</summary>
    public string DataDir => Path.Combine(GameDir, "Data");

    /// <summary><c>&lt;GameDir&gt;/Mods</c> — local mods.</summary>
    public string LocalModsDir => Path.Combine(GameDir, "Mods");

    /// <summary>Steam Workshop content dir for app 294100, if this is a Steam install.</summary>
    public string? WorkshopDir { get; init; }

    /// <summary>Builds the ordered scan roots: Data (Core/DLC) → Local → Workshop.</summary>
    public IReadOnlyList<ModSourceRoot> ToSourceRoots()
    {
        var roots = new List<ModSourceRoot>
        {
            new(DataDir, ModSource.Core),
            new(LocalModsDir, ModSource.Local),
        };
        if (WorkshopDir is not null)
        {
            roots.Add(new ModSourceRoot(WorkshopDir, ModSource.Workshop));
        }

        return roots;
    }
}
