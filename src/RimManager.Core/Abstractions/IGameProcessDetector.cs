namespace RimManager.Core.Abstractions;

/// <summary>
/// Detects whether RimWorld is currently running. Writing <c>ModsConfig.xml</c>
/// while the game is running is refused (engineering constraint #3): RimWorld
/// rewrites the file on exit and would clobber our change.
/// </summary>
public interface IGameProcessDetector
{
    bool IsGameRunning();
}

/// <summary>A detector that always reports "not running" — for tests and headless contexts.</summary>
public sealed class NeverRunningGameDetector : IGameProcessDetector
{
    public static readonly NeverRunningGameDetector Instance = new();

    public bool IsGameRunning() => false;
}
