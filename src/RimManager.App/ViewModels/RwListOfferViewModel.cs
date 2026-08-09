using RimManager.Core.Sharing;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the S-RWLIST task dialog (NF-10): one Workshop item that looks like a mod
/// list, its parsed facts, and the one question. Modal in the confirm family — closing
/// by any route other than the primary leaves <see cref="Accepted"/> false, so a
/// dismissed dialog can never be read as an import.
/// <para>
/// The payload is parsed <b>before</b> the dialog opens, because the card's facts
/// (list name, mod count) come from it — and a payload that does not parse is stated
/// plainly with the primary disabled: only well-formed lists can be imported, and the
/// file on disk is untouched either way.
/// </para>
/// </summary>
public sealed class RwListOfferViewModel
{
    /// <param name="checksumValid">
    /// Whether the payload's checksum matched its content. It has to reach the DIALOG:
    /// the mismatch used to be written to the status bar a line before this window
    /// opened, so the app detected edited or damaged content, said so where it could not
    /// be seen, and then asked for consent as though nothing were wrong.
    /// </param>
    public RwListOfferViewModel(
        RwListOffer offer, string fileName, RwList? list, string? parseError,
        bool checksumValid = true)
    {
        Offer = offer;
        FileName = fileName;
        List = list;
        ParseError = parseError;
        ChecksumMismatch = list is not null && !checksumValid;

        var mods = list?.Mods.Count() ?? 0;
        var separators = (list?.Entries.Length ?? 0) - mods;
        Facts = list is null
            ? fileName
            : $"{fileName} · {mods} mods · {separators} separators"
              + (string.IsNullOrWhiteSpace(list.Author) ? "" : $" · by {list.Author}")
              + (string.IsNullOrWhiteSpace(list.GameVersion) ? "" : $" · for {list.GameVersion}");
    }

    public RwListOffer Offer { get; }
    public string FileName { get; }
    public RwList? List { get; }
    public string? ParseError { get; }

    /// <summary>The item's Workshop name — the card's title.</summary>
    public string ItemName => Offer.ModName;

    /// <summary>One mono line: file · counts · author · game version.</summary>
    public string Facts { get; }

    /// <summary>
    /// The payload parsed, but its checksum does not match its content — it has been
    /// edited or damaged since it was exported.
    /// <para>
    /// Only meaningful when there IS a list: a payload that failed to parse has a
    /// parse error to show, and stacking a checksum complaint on top would be two
    /// alarms for one broken file.
    /// </para>
    /// </summary>
    public bool ChecksumMismatch { get; }

    public string ChecksumWarning =>
        "This file's checksum does not match its contents — it has been edited or "
        + "damaged since it was exported. Import it only if you trust where it came from.";

    public bool HasError => ParseError is not null;

    /// <summary>
    /// A mismatch does NOT block the import, deliberately: the list still parsed, and a
    /// hand-edited <c>.rwlist</c> is a legitimate thing to have. It has to be SAID
    /// before the click, which is what the dialog now does — the decision stays the
    /// user's, taken with the fact in front of them.
    /// </summary>
    public bool CanImport => !HasError;

    public const string Sentence =
        "The item stays in your inactive pane either way — importing creates a new "
        + "modlist and never activates anything.";

    /// <summary>Set only by the primary button; the view model never sets it.</summary>
    public bool Accepted { get; set; }
}
