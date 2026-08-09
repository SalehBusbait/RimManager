using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>The detail sidebar for the selected mod (spec §4.9 mod detail panel).</summary>
public sealed partial class ModDetailViewModel : ObservableObject, IModSourceBadge
{
    private readonly Action<ModMetadata> _save;
    private ModMetadata _metadata;

    public Mod Mod { get; }
    public string Name { get; }
    public string PackageId { get; }
    public string Authors { get; }

    /// <summary>The mod folder, for "Open folder".</summary>
    public string RootPath { get; }

    /// <summary>
    /// The Workshop id, when the mod came from there. Null hides the button.
    /// <para>
    /// The <i>id</i>, not a URL: which URL to open is a policy decision that depends on
    /// whether Steam is running, and it belongs in one place rather than baked in here.
    /// This used to hold a hand-built <c>https://</c> string — a second copy of
    /// <c>SteamUrls.WebFilePage</c>, and the reason this button always opened a browser.
    /// </para>
    /// </summary>
    public string? PublishedFileId { get; }

    public bool HasWorkshopUrl => PublishedFileId is not null;
    /// <summary>
    /// Never empty. A blank value beside a "Version" label reads as a failure to
    /// load rather than as "this mod does not declare one", which is the truth for
    /// a great many Workshop mods.
    /// </summary>
    public string Version { get; }
    public string SupportedVersions { get; }
    public string? Description { get; }
    public string Flags { get; }
    /// <summary>Dependencies with their resolved load positions (1a §6).</summary>
    public IReadOnlyList<DependencyRowViewModel> Dependencies { get; }

    /// <summary>
    /// The word beside the pill's icon (1a §3 draws the pill as mark + word).
    /// <para>
    /// Never <c>ModSource.ToString()</c>: the enum member is spelled <c>Dlc</c>, and
    /// every expansion's pill read "D · Dlc" — an identifier on screen. The letter it
    /// replaces is gone entirely; the pill carries the same icon the row does, so one
    /// mark means one thing everywhere it appears.
    /// </para>
    /// </summary>
    public string SourceLabel { get; }

    // --- IModSourceBadge -----------------------------------------------------
    // The same six bools the rows expose, so mod info renders through the SAME badge
    // template rather than a second copy of it.
    public bool IsCoreSource => Mod.Source == ModSource.Core;
    public bool IsDlcSource => Mod.Source == ModSource.Dlc;
    public bool IsWorkshopSource => Mod.Source == ModSource.Workshop;
    public bool IsLocalSource => Mod.Source == ModSource.Local;
    public bool IsGitSource => Mod.Source == ModSource.Git;

    /// <summary>The badge tooltip — the source in words.</summary>
    public string Source { get; }

    /// <summary>Where this mod sits, for the facts grid ("#118").</summary>
    public string LoadOrderText { get; }

    public bool HasPreview => Preview is not null;
    public bool HasDependencies => Dependencies.Count > 0;
    public bool HasDescription => Description is not null;

    /// <summary>
    /// The pane clamps the description to four lines, so a long one is cut off with
    /// no way to read the rest. The button is offered only when there IS a rest —
    /// measured by <see cref="DescriptionIsClamped"/> rather than shown always, so it
    /// never promises more text than exists.
    /// </summary>
    public bool DescriptionIsClamped => DescriptionClamp.IsClamped(Description);

    /// <summary>
    /// Bytes on disk, filled in when the walk finishes. Starts as an em dash rather
    /// than a spinner or "0 B": the pane's own convention for a fact not yet known,
    /// and a zero would be a wrong answer rather than an absent one.
    /// </summary>
    [ObservableProperty] private string _sizeText = "—";
    public Bitmap? Preview { get; }

    /// <summary>The file behind <see cref="Preview"/> — null unless the bitmap loaded,
    /// so the viewer is never offered an image that failed to decode.</summary>
    public string? PreviewPath { get; }

    /// <summary>Preview.png's size on disk, for the viewer's footer (N8).</summary>
    public long? PreviewBytes { get; }

    /// <summary>The crop's tooltip: what clicking opens, with the real pixel size —
    /// the number the 344×120 band is a crop of.</summary>
    public string PreviewTooltip => Preview is { } p
        ? $"View full size — {p.PixelSize.Width}×{p.PixelSize.Height}"
        : string.Empty;

    public string AboutXml { get; }

    [ObservableProperty] private string _notes;

    [ObservableProperty] private bool _favorite;

    public ModDetailViewModel(
        Mod mod, string? aboutXml, string? previewPath, ModMetadata metadata,
        Action<ModMetadata> save,
        int? loadPosition = null,
        Func<ModId, int?>? positionOf = null)
    {
        Mod = mod;
        _metadata = metadata;
        _save = save;

        Name = mod.Name;
        PackageId = mod.PackageId.Display;
        Version = string.IsNullOrWhiteSpace(mod.ModVersion) ? "—" : mod.ModVersion;
        SourceLabel = ModSourceText.Label(mod.Source);
        Source = ModSourceText.Describe(mod.Source);
        SupportedVersions = mod.SupportedVersions.IsDefaultOrEmpty
            ? "—"
            : string.Join(", ", mod.SupportedVersions);
        Authors = mod.Authors.IsDefaultOrEmpty ? "—" : string.Join(", ", mod.Authors);
        RootPath = mod.RootPath;
        PublishedFileId = mod.PublishedFileId;
        // Workshop descriptions arrive full of BBCode; rendering it raw fills the
        // pane with markup instead of the clamped summary it is meant to show.
        Description = BbCode.Strip(mod.Description);
        // §0a: the CDPTSL column went, and these words are where those facts live now.
        // An em dash rather than a blank when a mod ships none of them: an empty value
        // beside a label reads as "not looked at", which is a different claim.
        var content = string.Join(" · ", DescribeContent(mod));
        Flags = content.Length == 0 ? "—" : content;
        Dependencies = [.. mod.Dependencies.Select(d =>
            new DependencyRowViewModel(d, positionOf?.Invoke(d.PackageId)))];
        LoadOrderText = loadPosition is { } p ? $"#{p}" : "inactive";
        AboutXml = aboutXml ?? "(About.xml not found)";
        (Preview, PreviewBytes) = LoadPreview(previewPath);
        PreviewPath = Preview is null ? null : previewPath;

        _notes = metadata.Note ?? string.Empty;
        _favorite = metadata.Favorite;
    }

    /// <summary>Reveals the mod folder in the OS file manager (1a §9).</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        // FolderLauncher, not ShellUriLauncher: the URI launcher's allowlist rejects a
        // bare path by design, so this used to throw straight into the catch below and
        // reveal nothing. A folder deleted under us is still a no-op, but a deliberate one.
        new RimManager.Storage.FolderLauncher().Open(RootPath);
    }

    /// <summary>
    /// Opens the Workshop page. Hidden entirely for non-Workshop mods.
    /// <para>
    /// Through <c>WorkshopLinkService</c>, which prefers the running Steam client and
    /// falls back to the browser. This used to hand the browser URL straight to the
    /// launcher, so it opened a browser tab even with Steam up in the background.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void OpenWorkshop()
    {
        if (PublishedFileId is { } id) MainWindowViewModel.WorkshopLinks.Open(id);
    }

    /// <summary>
    /// Notes persist when the field loses focus (1a §7) — the binding uses
    /// UpdateSourceTrigger=LostFocus, so this fires once per edit rather than once
    /// per keystroke, and there is no Save button to forget.
    /// </summary>
    partial void OnNotesChanged(string value)
    {
        _metadata = _metadata with { Note = string.IsNullOrWhiteSpace(value) ? null : value };
        _save(_metadata);
    }

    partial void OnFavoriteChanged(bool value)
    {
        _metadata = _metadata with { Favorite = value };
        _save(_metadata);
    }

    // GlyphFor went with the letter badge: mod info draws the same icon the rows do.
    // Two spellings of one mark is how the two surfaces would come to disagree.

    private static IEnumerable<string> DescribeContent(Mod mod)
    {
        if (mod.HasAssemblies) yield return "C# code";
        if (mod.Content.HasFlag(ContentFlags.Patches)) yield return "XML patches";
        if (mod.Content.HasFlag(ContentFlags.Defs)) yield return "Defs";
        if (mod.Content.HasFlag(ContentFlags.Textures)) yield return "Textures";
        if (mod.Content.HasFlag(ContentFlags.Sounds)) yield return "Sounds";
        if (mod.Content.HasFlag(ContentFlags.Languages)) yield return "Translations";
        if (mod.IsTextureOnly) yield return "texture-only";
        if (mod.IsTranslationOnly) yield return "translation-only";
    }

    private static (Bitmap? Image, long? Bytes) LoadPreview(string? path)
    {
        if (path is null) return (null, null);
        try { return (new Bitmap(path), new System.IO.FileInfo(path).Length); }
        catch (Exception) { return (null, null); }
    }
}
