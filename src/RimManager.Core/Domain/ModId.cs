using System.Diagnostics.CodeAnalysis;

namespace RimManager.Core.Domain;

/// <summary>
/// A RimWorld <c>packageId</c>. RimWorld compares these case-insensitively, so
/// identity here is the lowercased form, while the original casing is preserved
/// for display (domain primer §3: normalize to lowercase everywhere, keep the
/// original for display).
/// </summary>
/// <remarks>
/// Confirmed against real data: <c>About.xml</c> ships <c>Jaxe.RimHUD</c> while
/// <c>ModsConfig.xml</c> lists <c>jaxe.rimhud</c> — the same mod. Comparing raw
/// strings would treat them as two mods.
/// </remarks>
public readonly struct ModId : IEquatable<ModId>
{
    /// <summary>Lowercased canonical form used for all identity comparisons.</summary>
    public string Value { get; }

    /// <summary>Original casing as authored, for display only.</summary>
    public string Display { get; }

    private ModId(string value, string display)
    {
        Value = value;
        Display = display;
    }

    public static ModId From(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var trimmed = packageId.Trim();
        return new ModId(trimmed.ToLowerInvariant(), trimmed);
    }

    public static bool TryFrom(string? packageId, out ModId id)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            id = default;
            return false;
        }

        id = From(packageId);
        return true;
    }

    public bool Equals(ModId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ModId other && Equals(other);

    public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Display ?? string.Empty;

    public static bool operator ==(ModId left, ModId right) => left.Equals(right);

    public static bool operator !=(ModId left, ModId right) => !left.Equals(right);
}
