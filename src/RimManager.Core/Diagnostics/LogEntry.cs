using System.Globalization;

namespace RimManager.Core.Diagnostics;

/// <summary>
/// Severity. Ordered, so a minimum-level filter is a comparison.
/// Settings ▸ Advanced exposes this as a segmented control (Error … Trace).
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
}

/// <summary>
/// The subsystem tags in use (<c>2f</c>). Deliberately a short, closed vocabulary:
/// the Activity tab filters on them and a free-for-all would make that useless.
/// </summary>
public static class LogSubsystem
{
    public const string Sort = "sort";
    public const string Validate = "valid";
    public const string Scan = "scan";
    public const string Steam = "steam";
    public const string Git = "git";
    public const string Io = "io";
    public const string Rules = "rules";
    public const string Ui = "ui";

    public static readonly string[] All = [Sort, Validate, Scan, Steam, Git, Io, Rules, Ui];
}

/// <summary>One line of the activity log.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Subsystem, string Message)
{
    /// <summary>2f colours a line BY LEVEL ONLY — never by subsystem.</summary>
    public bool IsError => Level == LogLevel.Error;
    public bool IsWarn => Level == LogLevel.Warn;

    /// <summary>
    /// The level exactly as the on-disk log writes it — upper-cased and padded to a
    /// fixed column. The Activity panel binds THIS rather than the enum: 2f says the
    /// panel is the same text as the file, and <c>Level.ToString()</c> quietly renders
    /// "Info" against the file's "INFO ", which is the drift the rule exists to stop.
    /// </summary>
    public string LevelText => LogEntryFormatter.LevelColumn(Level);
}

/// <summary>
/// Renders a <see cref="LogEntry"/> as the single canonical line.
/// <para>
/// The Activity tab and the on-disk log use <em>the same</em> formatter on purpose:
/// <c>2f</c> says the panel "is the same text as the on-disk log because its job is
/// to be pasted into a GitHub issue". Two formatters would drift, and the pasted
/// snippet would stop matching the file a maintainer asks for.
/// </para>
/// Shape: <c>HH:MM:SS.mmm LEVEL subsystem message</c>, with the level padded so the
/// columns line up in a monospace font.
/// </summary>
public static class LogEntryFormatter
{
    /// <summary>Width of the level column — "Error" is the longest at 5.</summary>
    private const int LevelWidth = 5;

    /// <summary>
    /// The level column, upper-cased and padded. Exposed so the Activity panel can
    /// render the identical string instead of a second, near-miss rendering of it.
    /// </summary>
    public static string LevelColumn(LogLevel level) =>
        level.ToString().ToUpperInvariant().PadRight(LevelWidth);

    public static string Format(LogEntry entry)
    {
        var time = entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return $"{time} {LevelColumn(entry.Level)} {entry.Subsystem} {entry.Message}";
    }

    /// <summary>Formats a whole run, newest last, for "Copy all" and the diagnostics bundle.</summary>
    public static string Format(IEnumerable<LogEntry> entries) =>
        string.Join(Environment.NewLine, entries.Select(Format));
}
