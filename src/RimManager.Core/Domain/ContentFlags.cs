namespace RimManager.Core.Domain;

/// <summary>
/// Concrete, directly-observed content of a mod folder (which top-level folders
/// exist). These are facts from the filesystem; higher-level derived
/// classifications (XML-only, texture-only, ...) are computed from them on
/// <see cref="Mod"/>.
/// </summary>
/// <remarks>
/// "Has Harmony patches" is deliberately NOT here: it requires reading
/// assemblies with Cecil, which is Phase 8. This enum stays cheap (file-existence
/// only), which is what the &lt;3s warm-scan target depends on.
/// </remarks>
[Flags]
public enum ContentFlags
{
    None = 0,
    Defs = 1 << 0,
    Patches = 1 << 1,
    Assemblies = 1 << 2,
    Textures = 1 << 3,
    Sounds = 1 << 4,
    Languages = 1 << 5,
    Sources = 1 << 6,

    /// <summary>A <c>.rwlist</c> file at the mod root — the Workshop-item-as-mod-list
    /// shape (NF-10). A fact only; <see cref="Mod.IsRwListItem"/> decides meaning.</summary>
    RwList = 1 << 7,
}
