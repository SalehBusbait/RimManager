using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Parsing;

/// <summary>
/// The pure result of parsing one <c>About.xml</c> — metadata only, no filesystem
/// provenance or content flags (the scanner adds those). <see cref="PackageId"/>
/// is nullable because a missing/malformed id is a common, non-fatal case that is
/// surfaced via <see cref="Warnings"/> rather than thrown.
/// </summary>
public sealed record AboutMetadata
{
    public string? PackageId { get; init; }
    public string? Name { get; init; }
    public ImmutableArray<string> Authors { get; init; } = [];
    public string? Description { get; init; }
    public ImmutableArray<string> SupportedVersions { get; init; } = [];
    public string? ModVersion { get; init; }
    public ImmutableArray<ModDependency> Dependencies { get; init; } = [];
    public ImmutableArray<string> LoadAfter { get; init; } = [];
    public ImmutableArray<string> LoadBefore { get; init; } = [];
    public ImmutableArray<string> ForceLoadAfter { get; init; } = [];
    public ImmutableArray<string> ForceLoadBefore { get; init; } = [];
    public ImmutableArray<string> IncompatibleWith { get; init; } = [];
    public ImmutableArray<ModWarning> Warnings { get; init; } = [];
}
