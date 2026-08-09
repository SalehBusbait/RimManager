using RimManager.Core.Abstractions;

namespace RimManager.Storage;

/// <summary>
/// <see cref="IFileWatcher"/> over <see cref="FileSystemWatcher"/>, with the coalescing the
/// interface demands.
/// </summary>
/// <remarks>
/// Watches the <b>directory</b> filtered to the filename, never the file itself. A watcher
/// bound to a path stops working the moment that path is replaced rather than appended to —
/// and replace-in-place is exactly how both RimWorld and our own <c>AtomicWriteAsync</c>
/// write, so a file-bound watcher would report the first change and then go quiet for ever.
/// </remarks>
public sealed class PhysicalFileWatcher : IFileWatcher
{
    /// <summary>
    /// How long to wait for the burst to finish. One save fires Changed two or three times,
    /// and our own atomic write adds a Created, a Changed and a Renamed — so the window has
    /// to outlast the whole sequence while still feeling immediate. 250ms does both.
    /// </summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    public IDisposable Watch(string path, Action onChanged)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(onChanged);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        var file = Path.GetFileName(path);

        // Nothing to watch is not a failure: the config folder may not exist yet on a
        // half-configured install, and the app must still start.
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return NoWatch.Instance;

        return new Subscription(directory, file, onChanged);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounce;
        private readonly Action _onChanged;
        private readonly Lock _gate = new();
        private bool _disposed;

        public Subscription(string directory, string file, Action onChanged)
        {
            _onChanged = onChanged;
            _debounce = new Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(directory, file)
            {
                // Size and LastWrite between them catch an in-place write; FileName catches
                // the create/rename half of a replace. Attributes are deliberately absent —
                // a read can change those and would wake this for nothing.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };

            _watcher.Changed += OnAny;
            _watcher.Created += OnAny;
            _watcher.Deleted += OnAny;
            _watcher.Renamed += OnAny;

            // Buffer overruns are possible on a busy folder. Losing an event is survivable
            // — it means one stale reading until the next change or the next reload — so
            // this must not throw into the process from a background thread.
            _watcher.Error += (_, _) => { };

            _watcher.EnableRaisingEvents = true;
        }

        private void OnAny(object sender, FileSystemEventArgs e) => Bump();

        private void Bump()
        {
            lock (_gate)
            {
                if (_disposed) return;

                // Restart, not schedule: each event pushes the deadline out, so a burst of
                // six fires the callback once, 250ms after the last of them.
                _debounce.Change(Quiet, Timeout.InfiniteTimeSpan);
            }
        }

        private void Fire(object? state)
        {
            lock (_gate)
            {
                if (_disposed) return;
            }

            // Outside the lock: the callback re-reads a file and may take a while, and
            // holding the gate would stall every incoming event behind it.
            try { _onChanged(); }
            catch { /* a watcher must never take the process down from a timer thread */ }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounce.Dispose();
        }
    }

    private sealed class NoWatch : IDisposable
    {
        public static readonly NoWatch Instance = new();
        public void Dispose() { }
    }
}
