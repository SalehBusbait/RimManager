namespace RimManager.Core.Abstractions;

/// <summary>
/// Watches one file for changes made by something other than us.
/// <para>
/// A seam rather than a <c>FileSystemWatcher</c> in the view model, for the same reason
/// <see cref="IFileSystem"/> is: this is I/O, and I/O has one home. It also makes the
/// interesting half testable — a fake fires the callback on demand, so "what happens when
/// the game rewrites the file" is a test rather than a thing you can only try by alt-tabbing
/// into RimWorld.
/// </para>
/// </summary>
public interface IFileWatcher
{
    /// <summary>
    /// Calls <paramref name="onChanged"/> when the file at <paramref name="path"/> is
    /// written, replaced, created or deleted. Dispose the result to stop watching.
    /// <para>
    /// Implementations <b>must coalesce</b>. A single save fires the platform watcher two or
    /// three times — and this project's own atomic write is a temp file, a copy and a
    /// replace, so watching the file we just wrote produces a burst every time. A caller
    /// that has to de-duplicate is a caller that will forget to.
    /// </para>
    /// <para>
    /// The callback may arrive on any thread. Marshalling is the caller's job, because only
    /// the caller knows whether it has a UI thread to marshal to.
    /// </para>
    /// </summary>
    IDisposable Watch(string path, Action onChanged);
}
