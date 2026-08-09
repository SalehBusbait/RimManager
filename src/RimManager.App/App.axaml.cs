using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RimManager.App.Services;
using RimManager.App.ViewModels;
using RimManager.Core.Abstractions;
using RimManager.Core.Diagnostics;
using RimManager.Storage.Diagnostics;
using RimManager.Integrations.Http;
using RimManager.Integrations.Processes;
using RimManager.Integrations.SteamCmd;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Where the on-disk log lives — beside the rest of the app's data rather than
    /// next to the executable, so it survives a reinstall and is somewhere a user
    /// can be pointed at ("Help ▸ Open log folder").
    /// </summary>
    private static string LogFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RimManager", "logs", "rimmanager.log");

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IFileSystem>(sp => new PhysicalFileSystem(sp.GetRequiredService<IClock>()));
        services.AddSingleton<IPlatformEnvironment, PlatformEnvironment>();
        services.AddSingleton<IFileWatcher, PhysicalFileWatcher>();
        services.AddSingleton<IHttpFetcher>(_ => new HttpClientFetcher());
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<ConflictAnalysisService>();
        services.AddSingleton<CollectionService>();
        services.AddSingleton<WorkshopDownloadService>();
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<RulesService>();
        services.AddSingleton<ModDatabasesService>();

        // Git (read-only except fetch) and the Integrations page's measurements. The
        // process seam is registered once: it is the only way Core is allowed to launch
        // anything, and a 30s timeout stops a git waiting on a credential prompt it can
        // never receive from hanging a scan.
        services.AddSingleton<IProcessRunner>(_ => new SystemProcessRunner());
        services.AddSingleton<GitService>();
        services.AddSingleton<IntegrationStatusService>();

        // The activity log (2f) and its on-disk mirror. Registered as one instance:
        // every subsystem writes here, the Activity dock tab reads the ring, and the
        // sink keeps the full record for pasting into an issue.
        services.AddSingleton(sp => new ActivityLog(sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new FileLogSink(
            sp.GetRequiredService<ActivityLog>(), LogFilePath()));

        services.AddTransient<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();

        // Resolve the sink eagerly: it attaches to the log's event on construction,
        // and anything logged before that would only live in memory.
        var log = provider.GetRequiredService<ActivityLog>();
        provider.GetRequiredService<FileLogSink>();
        log.Info(LogSubsystem.Ui, "RimManager starting");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            };

            // O17 · window geometry is restored HERE, in the composition root, and not
            // in the view model's ApplyLayout. The window is constructed and shown at
            // its markup literal long before InitializeAsync is awaited in OnLoaded, so
            // applying bounds there would be a visible jump from 1180x800 to wherever
            // the user left it. Reading layout.json costs one small file read on a path
            // that is already doing IO.
            try
            {
                var layout = new WorkspaceStateRepository(
                    provider.GetRequiredService<IFileSystem>()).LoadLayout();
                window.RestoreGeometry(layout);
            }
            catch (Exception ex)
            {
                // A window that opens centred is a far better failure than one that does
                // not open. Never fatal.
                log.Warn(LogSubsystem.Io, $"could not restore window geometry: {ex.Message}");
            }

            // The file-picker dialogs need a top-level; it only exists now.
            provider.GetRequiredService<FileDialogService>().Owner = window;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
