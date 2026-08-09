using System.Runtime.InteropServices;

namespace RimManager.Core.Abstractions;

/// <summary>
/// Platform-specific knowledge the locators need but Core must not hard-code:
/// where Steam/GOG typically install and where RimWorld keeps its config. The
/// real implementation (in Storage) reads the registry / environment; tests
/// supply a fake. All lists are ordered best-guess-first and may point at paths
/// that don't exist — callers verify against <see cref="IFileSystem"/>.
/// </summary>
public interface IPlatformEnvironment
{
    OSPlatform Platform { get; }

    /// <summary>Candidate Steam client install roots (each should contain <c>steamapps/</c>).</summary>
    IReadOnlyList<string> SteamClientRoots { get; }

    /// <summary>Best-effort GOG/DRM-free RimWorld install directories.</summary>
    IReadOnlyList<string> GogGameDirCandidates { get; }

    /// <summary>
    /// Ordered candidate RimWorld config directories (the one holding
    /// <c>ModsConfig.xml</c>), including Proton/Steam Deck layouts where relevant.
    /// </summary>
    IReadOnlyList<string> ConfigDirectoryCandidates { get; }
}
