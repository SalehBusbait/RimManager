using System.Runtime.InteropServices;
using RimManager.Core.Abstractions;

namespace RimManager.Core.Tests.Fakes;

public sealed class FakePlatformEnvironment : IPlatformEnvironment
{
    public OSPlatform Platform { get; init; } = OSPlatform.Linux;
    public IReadOnlyList<string> SteamClientRoots { get; init; } = [];
    public IReadOnlyList<string> GogGameDirCandidates { get; init; } = [];
    public IReadOnlyList<string> ConfigDirectoryCandidates { get; init; } = [];
}
