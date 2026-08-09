using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimManager.App.Services;

/// <summary>
/// A background writer that is <b>serialised</b> and <b>latest-wins</b>: writes never
/// overlap, the newest value always lands last, and a failure is reported rather than
/// thrown into a void.
/// <para>
/// This exists because the same bug was written twice. Anything that saves on every edit
/// — preferences on every toggle, tags on every keystroke — issues writes far faster than
/// a disk round-trip, and a bare <c>_ = SaveAsync(...)</c> then overlaps them. That went
/// wrong in both available ways:
/// </para>
/// <list type="bullet">
///   <item><b>Silently.</b> Five preference changes issued five concurrent atomic writes
///   and the snapshot from the second completed last, so the file held stale values while
///   the UI showed the new ones.</item>
///   <item><b>Loudly.</b> Two concurrent writes to <c>tags.json</c> collided inside
///   <c>File.Replace</c> — "Unable to remove the file to be replaced" — and because the
///   task was unawaited it took the whole app down.</item>
/// </list>
/// </summary>
public sealed class SerialWriter<T> where T : class
{
    private readonly Func<T, Task> _write;
    private readonly Action<Exception>? _onError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private T? _pending;

    /// <param name="onError">
    /// Where a failed write is reported. Required in spirit even though it is optional in
    /// signature: a save that fails silently is how a user loses work without being told,
    /// and a save that throws from an unawaited task kills the process.
    /// </param>
    public SerialWriter(Func<T, Task> write, Action<Exception>? onError = null)
    {
        _write = write;
        _onError = onError;
    }

    /// <summary>
    /// Queues <paramref name="value"/> as the state to end up on disk. Returns
    /// immediately — a save must never make a keystroke feel slow.
    /// </summary>
    public void Queue(T value)
    {
        Interlocked.Exchange(ref _pending, value);
        _ = FlushAsync();
    }

    /// <summary>Awaits any queued write. For tests and for shutdown.</summary>
    public async Task DrainAsync()
    {
        await FlushAsync().ConfigureAwait(false);
    }

    private async Task FlushAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Drain rather than write once: a value queued while a write is in flight is
            // picked up here, so the file always ends at the newest state.
            while (Interlocked.Exchange(ref _pending, null) is { } value)
            {
                await _write(value).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad. This runs on a task nobody awaits, so ANY escape is a
            // process kill — which is exactly how a colliding File.Replace crashed the app.
            _onError?.Invoke(ex);
        }
        finally
        {
            _gate.Release();
        }
    }
}
