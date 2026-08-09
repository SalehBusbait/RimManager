namespace RimManager.Core.Abstractions;

/// <summary>
/// A cheap stat result for one filesystem entry. The scanner keys its cache on
/// <see cref="LastWriteUtc"/> + <see cref="Size"/>, so these must be obtainable
/// without opening the file.
/// </summary>
/// <param name="FullPath">Absolute, platform-native path.</param>
/// <param name="IsDirectory">True for directories, false for files.</param>
/// <param name="Size">Length in bytes. Undefined (0) for directories.</param>
/// <param name="LastWriteUtc">Last-write timestamp in UTC.</param>
public readonly record struct FileEntry(
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastWriteUtc);
