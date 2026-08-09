using System.Collections.Generic;
using System.Threading.Tasks;
using RimManager.App.Tests.Fakes;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;

namespace RimManager.App.Tests;

/// <summary>
/// Builds a <see cref="SettingsViewModel"/> over fakes, and — the point of it —
/// <b>keeps every paths record the view model wrote</b>. Settings has no Save button any
/// more, so "did that edit reach disk" is a question about the writes it issued, not
/// about a command it exposes.
/// </summary>
public static class SettingsHarness
{
    public static (SettingsViewModel Vm, FakePreferences Prefs, List<InstallPaths> Saved) Build()
    {
        var prefs = new FakePreferences();
        var saved = new List<InstallPaths>();

        var paths = new InstallPaths { GameDir = "/game", ConfigDir = "/config" };

        var vm = new SettingsViewModel(
            paths, "none",
            p => { saved.Add(p); return Task.CompletedTask; },
            new StubFileSystem().WithDirectory("/game", "/config"),
            () => (null, null, null),
            prefs);

        return (vm, prefs, saved);
    }
}
