using Avalonia;
using RimManager.Integrations.Steamworks;

namespace RimManager.App;

internal static class Program
{
    // Avalonia requires a STA thread and needs to be initialized before any UI.
    [STAThread]
    public static int Main(string[] args)
    {
        // The Steamworks helper child. A session against the game's app id marks
        // RimWorld as RUNNING until the PROCESS exits — SteamAPI_Shutdown() does not
        // clear it — and Steam pauses downloads during gameplay, so the session
        // lives in this short-lived child and its exit is what lets the client
        // start downloading while the app stays open. Routed before any UI exists.
        if (args.Length > 0 && args[0] == SteamworksDownload.ArgumentMarker)
            return SteamworksDownload.Run(args[1..]);

        // Same child-process rules, different job: creates a PRIVATE collection on
        // the user's Workshop account (NF-10 slice 4) and prints its id to stdout.
        if (args.Length > 0 && args[0] == SteamworksCollectionCreate.ArgumentMarker)
            return SteamworksCollectionCreate.Run(args[1..]);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
