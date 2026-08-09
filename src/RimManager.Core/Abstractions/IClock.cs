namespace RimManager.Core.Abstractions;

/// <summary>
/// Abstraction over "now". Nothing in the domain calls <see cref="DateTimeOffset.UtcNow"/>
/// directly, so timestamped backups, snapshot times, and export <c>createdUtc</c>
/// fields are all deterministic under test.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
