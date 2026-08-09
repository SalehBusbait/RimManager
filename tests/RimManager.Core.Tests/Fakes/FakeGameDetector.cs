using RimManager.Core.Abstractions;

namespace RimManager.Core.Tests.Fakes;

public sealed class FakeGameDetector(bool running) : IGameProcessDetector
{
    public bool Running { get; set; } = running;

    public bool IsGameRunning() => Running;
}
