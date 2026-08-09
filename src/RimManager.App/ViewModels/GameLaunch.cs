using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace RimManager.App.ViewModels;

/// <summary>What to run, already split into a program and its arguments.</summary>
/// <param name="FileName">The executable, unquoted.</param>
/// <param name="Arguments">
/// Arguments as separate strings. Kept split rather than joined because they are passed
/// through <c>ProcessStartInfo.ArgumentList</c>: a game folder with a space in its name
/// must not turn into two arguments, and that is exactly what a joined string does.
/// </param>
public sealed record LaunchPlan(string FileName, ImmutableArray<string> Arguments);

/// <summary>
/// Turns Settings ▸ Integrations ▸ Game launch into something runnable (<c>2g</c>).
/// <para>
/// The command is a template holding <c>%args%</c>, which is where the user's extra
/// arguments go. A placeholder rather than "we always append": the Steam form is
/// <c>steam -applaunch 294100 %args%</c>, and arguments appended after the AppID would be
/// read by Steam rather than passed to RimWorld.
/// </para>
/// <para>
/// Pure, so the quoting rules are testable without launching anything. Getting them
/// wrong means either a game that will not start or, worse, running the wrong program.
/// </para>
/// </summary>
public static class GameLaunch
{
    public const string ArgsPlaceholder = "%args%";

    /// <summary>
    /// The bare-<c>steam</c> form <c>2g</c> shows.
    /// <para>
    /// It is a <b>Linux</b> command. On Linux <c>steam</c> is on <c>PATH</c>; on Windows it
    /// is not — Steam installs to <c>C:\Program Files (x86)\Steam\steam.exe</c> and adds
    /// nothing to <c>PATH</c>, so this shipped as a default that could never run. Kept
    /// only as the last-resort fallback, and as the value <see cref="NeedsReseeding"/>
    /// recognises so an already-saved copy of it gets corrected.
    /// </para>
    /// </summary>
    public const string SteamTemplate = "steam -applaunch 294100 %args%";

    /// <summary>RimWorld's Steam AppID.</summary>
    public const string AppId = "294100";

    /// <summary>The game executable for this platform, inside the install folder.</summary>
    public static string GameExecutable(string gameDir) => System.IO.Path.Combine(
        gameDir,
        OperatingSystem.IsWindows() ? "RimWorldWin64.exe"
        : OperatingSystem.IsMacOS() ? "RimWorldMac.app"
        : "RimWorldLinux");

    /// <summary>
    /// The default command for an instance.
    /// <para>
    /// A Steam install is handed to Steam so playtime is recorded and the overlay works —
    /// but only when we can name Steam's <b>executable</b>, because assuming it is on
    /// <c>PATH</c> is exactly the bug this replaced. With no Steam to name, running the
    /// game directly is the option that actually starts something.
    /// </para>
    /// </summary>
    /// <param name="steamExe">Steam's executable, or null when it was not found.</param>
    public static string DefaultTemplate(string? gameDir, bool isSteamInstall, string? steamExe = null)
    {
        if (isSteamInstall && !string.IsNullOrWhiteSpace(steamExe))
            return $"{Quote(steamExe)} -applaunch {AppId} {ArgsPlaceholder}";

        if (string.IsNullOrWhiteSpace(gameDir)) return SteamTemplate;

        return Quote(GameExecutable(gameDir)) + " " + ArgsPlaceholder;
    }

    /// <summary>
    /// Whether a stored command is the un-runnable bare-<c>steam</c> default rather than
    /// something the user chose. Seeding happens once so an edited command is never
    /// discarded — but a default that cannot work is not worth preserving, so this one
    /// value is re-seeded.
    /// </summary>
    public static bool NeedsReseeding(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
        || (string.Equals(stored.Trim(), SteamTemplate, StringComparison.OrdinalIgnoreCase)
            && !OperatingSystem.IsLinux());

    /// <summary>
    /// Splits a template into a plan, substituting <paramref name="extraArgs"/> for
    /// <c>%args%</c>. Returns null when there is no program to run — an empty command is
    /// a misconfiguration to report, never a process to guess at.
    /// </summary>
    public static LaunchPlan? Parse(string? template, string? extraArgs)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;

        var tokens = Tokenize(template);
        if (tokens.Count == 0) return null;

        var fileName = tokens[0];
        var extra = Tokenize(extraArgs ?? string.Empty);
        var arguments = new List<string>();

        foreach (var token in tokens.GetRange(1, tokens.Count - 1))
        {
            // The placeholder expands to zero or more arguments. Zero is the normal case
            // — most people set no extra arguments — and it must leave nothing behind,
            // not an empty string the game would see as a blank argument.
            if (token == ArgsPlaceholder) arguments.AddRange(extra);
            else arguments.Add(token);
        }

        return new LaunchPlan(fileName, [.. arguments]);
    }

    /// <summary>
    /// Splits on whitespace, honouring double quotes so a path with spaces stays one
    /// token. Quotes are removed: <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>
    /// re-quotes each argument itself, and leaving them in makes the program name literally
    /// contain a quote character.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;
}
