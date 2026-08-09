using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RimManager.Core.Abstractions;

namespace RimManager.Storage.Persistence;

/// <summary>Base type for persistence failures.</summary>
public class PersistenceException : Exception
{
    public PersistenceException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The document is malformed. Recoverable from a backup; if none works, this is the final error.</summary>
public sealed class CorruptDocumentException : PersistenceException
{
    public CorruptDocumentException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The document was written by a newer RimManager. This is NOT corruption — do not
/// recover from a backup; surface it so the user upgrades.
/// </summary>
public sealed class SchemaTooNewException : PersistenceException
{
    public SchemaTooNewException(string message) : base(message) { }
}

/// <summary>
/// Loads and saves a versioned JSON document, the single mechanism behind
/// engineering constraint #5: user data is validated on load, backed up before
/// every overwrite, and recovered from a backup if the primary file is corrupt —
/// so a user's separators/tags/profiles are never silently lost.
/// </summary>
/// <remarks>
/// On disk each file is <c>{ "schemaVersion": N, "data": { ... } }</c>. The domain
/// types stay version-free; migrations rewrite the raw <c>data</c> node before
/// deserialization. <typeparamref name="T"/> is the payload type.
/// </remarks>
public sealed class JsonDocumentStore<T> where T : class
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IFileSystem _fs;
    private readonly int _currentVersion;
    private readonly IReadOnlyList<Func<JsonObject, JsonObject>> _migrations;
    private readonly JsonSerializerOptions _options;

    /// <param name="migrations">
    /// <c>migrations[i]</c> upgrades a document from schema version <c>i+1</c> to
    /// <c>i+2</c>. The current version is <c>migrations.Count + 1</c>.
    /// </param>
    public JsonDocumentStore(
        IFileSystem fs,
        IReadOnlyList<Func<JsonObject, JsonObject>>? migrations = null,
        JsonSerializerOptions? options = null)
    {
        _fs = fs;
        _migrations = migrations ?? [];
        _currentVersion = _migrations.Count + 1;
        _options = options ?? JsonSettings.Options;
    }

    /// <summary>Loads the document, or null if the file does not exist.</summary>
    public T? Load(string path)
    {
        if (!_fs.FileExists(path)) return null;

        try
        {
            return Deserialize(_fs.ReadAllText(path));
        }
        catch (SchemaTooNewException)
        {
            throw; // not corruption — never "recover" a newer file
        }
        catch (Exception ex) when (ex is JsonException or CorruptDocumentException or FormatException)
        {
            return RecoverFromBackup(path, ex);
        }
    }

    /// <param name="backup">
    /// Defaults to true — constraint #5 backs up user data before every overwrite.
    /// <para>
    /// Pass false only for documents that are cheap to recreate and written often. The
    /// UI preference file is the case: it changes on every toggle flip, and a timestamped
    /// backup per flip would bury the profile backups that actually matter.
    /// </para>
    /// </param>
    public async Task SaveAsync(string path, T data, bool backup = true, CancellationToken ct = default)
    {
        var envelope = new JsonObject
        {
            ["schemaVersion"] = _currentVersion,
            ["data"] = JsonSerializer.SerializeToNode(data, _options),
        };

        var json = envelope.ToJsonString(_options);
        await _fs.AtomicWriteAsync(path, Utf8NoBom.GetBytes(json), backup, ct).ConfigureAwait(false);
    }

    private T Deserialize(string text)
    {
        var node = JsonNode.Parse(text) as JsonObject
            ?? throw new CorruptDocumentException("Document root is not a JSON object.");

        var version = node["schemaVersion"]?.GetValue<int>() ?? 1;
        if (version > _currentVersion)
        {
            throw new SchemaTooNewException(
                $"Document schema version {version} is newer than supported ({_currentVersion}). " +
                "Upgrade RimManager to read it.");
        }

        var data = node["data"] as JsonObject
            ?? throw new CorruptDocumentException("Document has no 'data' object.");

        // Apply migrations from the file's version up to current.
        for (int v = version; v < _currentVersion; v++)
        {
            data = _migrations[v - 1](data);
        }

        return data.Deserialize<T>(_options)
            ?? throw new CorruptDocumentException("Document 'data' deserialized to null.");
    }

    private T RecoverFromBackup(string path, Exception primaryError)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var prefix = Path.GetFileName(path) + ".";

        var backups = _fs.EnumerateEntries(directory)
            .Where(e => !e.IsDirectory)
            .Select(e => e.FullPath)
            .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)
                        && p.EndsWith(".bak", StringComparison.Ordinal))
            .OrderByDescending(p => p, StringComparer.Ordinal); // timestamped names -> newest first

        foreach (var backup in backups)
        {
            try
            {
                return Deserialize(_fs.ReadAllText(backup));
            }
            catch (Exception ex) when (ex is JsonException or CorruptDocumentException or FormatException)
            {
                // try the next-oldest backup
            }
        }

        throw new CorruptDocumentException(
            $"'{path}' is corrupt and no usable backup was found.", primaryError);
    }
}
