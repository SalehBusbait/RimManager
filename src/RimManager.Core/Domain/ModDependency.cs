namespace RimManager.Core.Domain;

/// <summary>
/// A declared dependency from <c>About.xml</c> (<c>modDependencies</c>). All URL
/// fields are optional and appear in inconsistent shapes across mods (some use
/// <c>steam://</c>, some a full <c>steamcommunity.com</c> link).
/// </summary>
public sealed record ModDependency(
    ModId PackageId,
    string? DisplayName = null,
    string? SteamWorkshopUrl = null,
    string? DownloadUrl = null);
