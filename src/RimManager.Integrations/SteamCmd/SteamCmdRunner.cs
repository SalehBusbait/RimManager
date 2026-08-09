using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.Integrations.SteamCmd;

/// <summary>The full result of one SteamCMD invocation.</summary>
public sealed record SteamCmdRunResult(
    int ExitCode,
    string Output,
    ImmutableArray<WorkshopDownloadResult> Results);

/// <summary>
/// Drives a SteamCMD executable to download Workshop items <em>anonymously</em> —
/// RimWorld (294100) permits it, so no account or credentials are involved. Wraps the
/// process (spawn, capture output, parse) and lives in <c>RimManager.Integrations</c>
/// because it shells out; the outcome parsing is the pure <see cref="SteamCmdOutputParser"/>.
/// </summary>
public sealed class SteamCmdRunner(string exePath)
{
    private readonly string _exePath = exePath;

    /// <summary>
    /// Runs <c>+login anonymous +workshop_download_item &lt;appId&gt; &lt;id&gt;… +quit</c>
    /// for all ids in one session (one login). <paramref name="onLine"/> receives each
    /// output line live (for progress); the full output is also returned and parsed.
    /// </summary>
    public async Task<SteamCmdRunResult> DownloadWorkshopItemsAsync(
        IReadOnlyCollection<string> publishedFileIds,
        int appId,
        string? forceInstallDir = null,
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(publishedFileIds);
        if (!File.Exists(_exePath)) throw new FileNotFoundException("SteamCMD executable not found.", _exePath);

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            WorkingDirectory = Path.GetDirectoryName(_exePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (forceInstallDir is not null)
        {
            psi.ArgumentList.Add("+force_install_dir");
            psi.ArgumentList.Add(forceInstallDir);
        }

        psi.ArgumentList.Add("+login");
        psi.ArgumentList.Add("anonymous");
        foreach (var id in publishedFileIds)
        {
            psi.ArgumentList.Add("+workshop_download_item");
            psi.ArgumentList.Add(appId.ToString());
            psi.ArgumentList.Add(id);
        }

        psi.ArgumentList.Add("+quit");

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var gate = new object();

        void OnData(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (gate) output.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        }

        process.OutputDataReceived += OnData;
        process.ErrorDataReceived += OnData;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        string text;
        lock (gate) text = output.ToString();
        return new SteamCmdRunResult(process.ExitCode, text, SteamCmdOutputParser.Parse(text));
    }
}
