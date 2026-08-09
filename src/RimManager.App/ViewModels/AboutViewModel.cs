using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using RimManager.Core.Diagnostics;

namespace RimManager.App.ViewModels;

/// <summary>
/// About (<c>2i</c>-9). Everything here is <b>read from the running assembly</b> rather
/// than written down: a hand-typed version line is wrong from the first release that
/// forgets to update it, and this is the line people paste into bug reports.
/// </summary>
public sealed class AboutViewModel
{
    public AboutViewModel()
    {
        // The app's own stamp KEEPS the commit, short (N9): every pre-release build
        // shares one version number, so the commit is the only thing that says which
        // build a report is about — and this line is the report's first line. The
        // earlier cut trimmed it here while calling this "the one line a bug report
        // needs", which was the contradiction N9 resolved. Avalonia's stays trimmed:
        // a dependency's build metadata identifies nothing we ship.
        Version = BuildStamp.ForAssembly(typeof(AboutViewModel).Assembly);
        AvaloniaVersion = Read(typeof(Avalonia.Application).Assembly) ?? "unknown";
        Runtime = RuntimeInformation.FrameworkDescription;
        Platform = RuntimeInformation.RuntimeIdentifier;

        VersionLine = $"{Version} · Avalonia {AvaloniaVersion} · {Runtime} · {Platform}";
    }

    public string Version { get; }
    public string AvaloniaVersion { get; }
    public string Runtime { get; }
    public string Platform { get; }

    /// <summary>The mark for the CURRENT theme (T4) — About opens after the hub has
    /// applied it, so the construction-time read is always fresh. Null under headless
    /// tests, where an empty Image is the right rendering of "no assets".</summary>
    public Avalonia.Media.Imaging.Bitmap? Mark { get; } = Themes.ThemeAssets.CurrentMark();

    /// <summary>The one line a bug report needs.</summary>
    public string VersionLine { get; }

    public const string ProjectUrl = "https://github.com/SalehBusbait/RimManager";

    /// <summary>
    /// N12 · the licence, said on the one screen that exists to answer "what is this".
    /// <para>
    /// Named rather than reproduced: the full text is <c>LICENSE</c> at the repository
    /// root, and a wall of legal text in a 34px-mark dialog is read by nobody. What a
    /// reader needs here is which licence it is and that the dependencies keep their
    /// own — the second half being the part people get wrong about MIT.
    /// </para>
    /// </summary>
    public const string LicenceText =
        "RimManager is free and open source under the MIT licence — use it, change it, "
        + "ship your own version. The libraries it is built on keep their own licences; "
        + "THIRD-PARTY-NOTICES.md in the repository lists every one.";

    /// <summary>An instance view of <see cref="LicenceText"/>, for the same reason
    /// <see cref="Credits"/> exists: a compiled binding cannot reach a const.</summary>
    public string Licence => LicenceText;

    /// <summary>
    /// The credit and the disclaimer, both required (<c>2i</c>-9). The community-rules
    /// credit is not politeness — the sort quality this app is judged on comes largely
    /// from RimSort's database, and the Ludeon disclaimer matters because a mod manager
    /// that looks official is one whose bugs get reported to the wrong people.
    /// </summary>
    public const string CreditsText =
        "Sorting uses the community rules database maintained by the RimSort project, "
        + "fetched and cached locally; RimManager adds nothing to it and claims no ownership "
        + "of it. RimManager is not affiliated with, endorsed by, or connected to Ludeon "
        + "Studios. RimWorld is Ludeon's.";

    /// <summary>An instance view of <see cref="CreditsText"/>: a compiled binding
    /// cannot reach a const.</summary>
    public string Credits => CreditsText;

    /// <summary>
    /// Reads a DEPENDENCY's informational version, trimmed of build metadata — a
    /// dependency's SHA identifies nothing we ship. The app's own version goes through
    /// <see cref="BuildStamp"/> instead, which keeps the commit for exactly the
    /// opposite reason.
    /// </summary>
    private static string? Read(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3);
    }
}
