using RimManager.Core.Abstractions;

namespace RimManager.Storage;

/// <summary>Real wall-clock. The only place <see cref="DateTimeOffset.UtcNow"/> is read.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
