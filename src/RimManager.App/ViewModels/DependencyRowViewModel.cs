using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row of the Mod Info dependencies block (1a §6): a ✓ / ✕ glyph, the name, and
/// the dependency's position in the load order right-aligned.
/// <para>
/// The index is the point of the block. Knowing a dependency is <em>present</em> is
/// half the answer; knowing it loads at #4 while this mod loads at #118 is what
/// tells you the order is actually right.
/// </para>
/// </summary>
public sealed class DependencyRowViewModel
{
    public DependencyRowViewModel(ModDependency dependency, int? position)
    {
        Name = dependency.DisplayName ?? dependency.PackageId.Display;
        PackageId = dependency.PackageId;
        Position = position;
    }

    public string Name { get; }
    public ModId PackageId { get; }

    /// <summary>Null when the dependency is not installed at all.</summary>
    public int? Position { get; }

    public bool IsSatisfied => Position is not null;
    public bool IsMissing => Position is null;

    /// <summary>"#4", or the missing note. A missing dependency renders in danger
    /// with a "Find" link (1a §6).</summary>
    public string PositionText => Position is { } p ? $"#{p}" : "not installed";
}
