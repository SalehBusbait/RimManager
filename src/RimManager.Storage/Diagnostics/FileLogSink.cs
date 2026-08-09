using System.Text;
using RimManager.Core.Diagnostics;

namespace RimManager.Storage.Diagnostics;

/// <summary>
/// Mirrors an <see cref="ActivityLog"/> to a file on disk.
/// <para>
/// The on-disk log is the full record: the in-memory ring drops its oldest lines
/// once a session gets long, and the one thing a maintainer will ask for is the
/// whole file. It uses the same <see cref="LogEntryFormatter"/> as the Activity tab,
/// so what the user copies out of the panel is byte-identical to what is on disk
/// (<c>2f</c>).
/// </para>
/// <para>
/// Lives in <c>Storage</c>, not <c>Core</c>: <c>Core</c> performs no I/O. It attaches
/// to the log's event rather than being called directly, so nothing in the domain
/// has to know a file exists.
/// </para>
/// </summary>
public sealed class FileLogSink : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly ActivityLog _log;
    private readonly string _path;
    private StreamWriter? _writer;
    private bool _disposed;

    /// <summary>Rolls the file once it passes this size, keeping one previous copy.</summary>
    public const long MaxBytes = 4 * 1024 * 1024;

    public FileLogSink(ActivityLog log, string path)
    {
        _log = log;
        _path = path;

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        Roll();
        Open();

        // Backfill anything logged before the sink attached (startup happens early).
        foreach (var entry in log.Snapshot()) Append(entry);

        _log.EntryWritten += Append;
    }

    /// <summary>Where the log is written — what Help ▸ Open log folder reveals.
    /// Named FilePath, not Path, so it does not shadow <see cref="System.IO.Path"/>.</summary>
    public string FilePath => _path;

    private void Open() =>
        _writer = new StreamWriter(
            new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            Utf8NoBom) { AutoFlush = true };

    /// <summary>
    /// Keeps exactly one previous log. A crash report wants the current session and
    /// the one before it; more than that is clutter nobody reads.
    /// </summary>
    private void Roll()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length < MaxBytes) return;

            var previous = _path + ".1";
            File.Delete(previous);
            File.Move(_path, previous);
        }
        catch (IOException)
        {
            // A locked or unrollable log must never take the app down with it.
        }
    }

    private void Append(LogEntry entry)
    {
        lock (_gate)
        {
            if (_disposed || _writer is null) return;

            try
            {
                _writer.WriteLine(LogEntryFormatter.Format(entry));
            }
            catch (IOException)
            {
                // Logging is never worth crashing over — a full disk or a lock loses
                // the line, not the session.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _log.EntryWritten -= Append;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
