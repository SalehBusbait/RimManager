using System.IO;
using System.Linq;

namespace RimManager.App.Tests;

/// <summary>
/// Locates the repository root from the test assembly's location by walking up to
/// the solution file. The theme guards below are source-file checks — they read
/// the .axaml as text rather than loading it through Avalonia, so they need no UI
/// thread and run in a bare clone.
/// </summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string AppProject => Path.Combine(Root, "src", "RimManager.App");
    public static string Themes => Path.Combine(AppProject, "Themes");

    /// <summary>
    /// The hub view model as ONE text, concatenated across its partial files
    /// (N11 split MainWindowViewModel by surface). Source checks that ask "does the
    /// hub contain / how often does the hub do X" must read this, not one file —
    /// reading only the spine is how five guards went blind the day of the split.
    /// </summary>
    public static string HubSource() => string.Concat(
        Directory.EnumerateFiles(Path.Combine(AppProject, "ViewModels"), "MainWindowViewModel*.cs")
            .OrderBy(f => f)
            .Select(File.ReadAllText));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RimManager.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find RimManager.slnx above '{AppContext.BaseDirectory}'.");
    }
}
