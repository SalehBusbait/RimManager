using RimManager.Core.Domain;

namespace RimManager.Core.Abstractions;

/// <summary>
/// A persistent cache of parsed mods, keyed on the mod's <c>About.xml</c> path and
/// its stat. This is what makes the warm scan fast (spec §4.9): if the About.xml
/// hasn't changed (same mtime + size), reuse the parsed <see cref="Mod"/> instead
/// of re-reading and re-parsing.
/// </summary>
/// <remarks>
/// Per the storage decision, this is a <em>disposable derived cache</em> — losing
/// it costs only a re-scan, never user data. A no-op implementation is always a
/// valid (if slow) choice.
/// </remarks>
public interface IModCache
{
    /// <summary>
    /// Returns the cached mod for <paramref name="aboutXmlPath"/> if present and its
    /// stat matches <paramref name="stat"/> (mtime + size); otherwise null.
    /// </summary>
    Mod? TryGet(string aboutXmlPath, FileEntry stat);

    /// <summary>Stores/updates the cache entry for a mod.</summary>
    void Put(string aboutXmlPath, FileEntry stat, Mod mod);

    /// <summary>Persists any buffered writes. No-op for in-memory implementations.</summary>
    void Flush();
}

/// <summary>An <see cref="IModCache"/> that never caches. The safe default.</summary>
public sealed class NullModCache : IModCache
{
    public static readonly NullModCache Instance = new();

    public Mod? TryGet(string aboutXmlPath, FileEntry stat) => null;

    public void Put(string aboutXmlPath, FileEntry stat, Mod mod) { }

    public void Flush() { }
}
