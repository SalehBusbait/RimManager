using RimManager.Core.Abstractions;

namespace RimManager.Core.Tests.Fakes;

/// <summary>Deterministic clock for tests.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
