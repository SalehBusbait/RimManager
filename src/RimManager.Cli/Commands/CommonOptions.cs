using System.CommandLine;

namespace RimManager.Cli.Commands;

/// <summary>The install/config/cache options shared by every read command.</summary>
internal sealed class CommonOptions
{
    public required Option<string?> GameDir { get; init; }
    public required Option<string?> WorkshopDir { get; init; }
    public required Option<string?> ConfigDir { get; init; }
    public required Option<bool> NoCache { get; init; }

    public static CommonOptions Create() => new()
    {
        GameDir = new Option<string?>("--game-dir") { Description = "Override the RimWorld game directory." },
        WorkshopDir = new Option<string?>("--workshop-dir") { Description = "Override the Steam Workshop content directory." },
        ConfigDir = new Option<string?>("--config-dir") { Description = "Override the config directory (holds ModsConfig.xml)." },
        NoCache = new Option<bool>("--no-cache") { Description = "Bypass the on-disk scan cache." },
    };

    public void AddTo(Command command)
    {
        command.Options.Add(GameDir);
        command.Options.Add(WorkshopDir);
        command.Options.Add(ConfigDir);
        command.Options.Add(NoCache);
    }
}
