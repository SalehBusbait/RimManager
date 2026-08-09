using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RimManager.App.Services;

/// <summary>
/// Thin wrapper over the top-level window's <see cref="IStorageProvider"/> for save/open
/// file pickers. The one view-coupled piece of the share flow — its <see cref="Owner"/>
/// is set once the main window exists; the export/import <em>logic</em> stays in the
/// (Core-tested) <c>Sharing</c> layer.
/// </summary>
public sealed class FileDialogService
{
    /// <summary>The window whose storage provider hosts the dialogs; set at startup.</summary>
    public TopLevel? Owner { get; set; }

    public async Task<string?> SaveAsync(string title, string suggestedName, string extension)
    {
        if (Owner?.StorageProvider is not { CanSave: true } storage) return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType($"{extension} file") { Patterns = [$"*.{extension}"] }],
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// A folder picker, for the paths the app is configured with. Lives here rather than
    /// in a window's code-behind because <c>2k</c>'s game-not-found state offers
    /// "Locate folder…" from the view model, and a second copy of this in code-behind is
    /// how two surfaces end up disagreeing about what they accept.
    /// </summary>
    public async Task<string?> PickFolderAsync(string title)
    {
        if (Owner?.StorageProvider is not { CanPickFolder: true } storage) return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> OpenAsync(string title, params string[] extensions)
    {
        if (Owner?.StorageProvider is not { CanOpen: true } storage) return null;

        var patterns = extensions.Select(e => $"*.{e}").ToArray();
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Modlist") { Patterns = patterns }],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
