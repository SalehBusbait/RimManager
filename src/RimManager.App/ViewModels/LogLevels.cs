using System.Collections.Immutable;
using RimManager.Core.Diagnostics;

namespace RimManager.App.ViewModels;

/// <summary>
/// The log-level segmented control on Settings ▸ Advanced (<c>2g</c>), mapped onto
/// <see cref="LogLevel"/>.
/// <para>
/// Ordered loudest-first, the way the control reads left to right: <b>Error</b> is the
/// narrowest view and <b>Trace</b> the widest. An index is stored rather than the enum's
/// numeric value, so reordering the control cannot silently reinterpret a saved setting.
/// </para>
/// </summary>
public static class LogLevels
{
    public static readonly ImmutableArray<(string Label, LogLevel Level)> Choices =
    [
        ("Error", LogLevel.Error),
        ("Warn", LogLevel.Warn),
        ("Info", LogLevel.Info),
        ("Debug", LogLevel.Debug),
        ("Trace", LogLevel.Trace),
    ];

    /// <summary>Info — the normal reading level. Debug and Trace are for reproducing
    /// something, not for living at.</summary>
    public const int DefaultIndex = 2;

    public static int Clamp(int index) => index < 0 || index >= Choices.Length ? DefaultIndex : index;

    /// <summary>
    /// Applies the floor and says so IN the log, at a level the new floor admits.
    /// <para>
    /// The announcement used to go out at Info after the floor landed, so setting the
    /// floor to Error silenced its own announcement — the one line that would explain a
    /// quiet log was the first line the change dropped, and an unchosen Error floor sat
    /// unexplained on the owner's install for four days because of it. Announcing at
    /// the louder of Info and the new floor survives every transition, including
    /// Warn→Error, where neither Info under the old floor nor Info under the new one
    /// would get through.
    /// </para>
    /// </summary>
    public static void ApplyFloor(IActivityLog log, int index)
    {
        var level = Level(index);
        log.MinimumLevel = level;

        var announce = level > LogLevel.Info ? level : LogLevel.Info;
        log.Write(announce, LogSubsystem.Ui, $"Log level set to {Label(index)}");
    }

    public static LogLevel Level(int index) => Choices[Clamp(index)].Level;

    public static string Label(int index) => Choices[Clamp(index)].Label;

    /// <summary>
    /// What raising the floor actually costs, said on the page. Trace on a 400-mod scan is
    /// tens of thousands of lines, and a user who leaves it there wonders later why the
    /// log file is enormous.
    /// </summary>
    public static string Note(int index) => Level(index) switch
    {
        LogLevel.Trace => "Trace records everything, including every file the scanner touches. Useful for one reproduction, noisy to leave on.",
        LogLevel.Debug => "Debug adds the decisions behind each sort and scan. This is the level to reproduce a bug at.",
        LogLevel.Error => "Only failures. The Activity tab will look empty most of the time, which is the point.",
        LogLevel.Warn => "Failures and things that merely look wrong.",
        _ => "The normal level: what happened, without how it was decided.",
    };
}
