using RimManager.Core.Domain;

namespace RimManager.Core.Scanning;

/// <summary>
/// A directory to scan for mods, tagged with the provenance its children get.
/// </summary>
/// <param name="Path">Directory containing one or more mod folders.</param>
/// <param name="Source">
/// Provenance for mods found here. For <see cref="ModSource.Core"/> roots (the
/// game's <c>Data</c> dir), the scanner refines Core vs. Dlc per packageId.
/// </param>
public sealed record ModSourceRoot(string Path, ModSource Source);
