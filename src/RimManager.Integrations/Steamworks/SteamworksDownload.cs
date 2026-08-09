using System.Globalization;
using System.Runtime.InteropServices;

namespace RimManager.Integrations.Steamworks;

/// <summary>
/// The child-process half of the Workshop updater: asks the Steam client to download
/// today's version of each item, then <b>exits, which is the point</b>. A session
/// against the game's app id marks RimWorld as RUNNING in the Steam client, and
/// <c>SteamAPI_Shutdown()</c> does <b>not</b> clear that — the client watches the
/// <i>process</i>, and only its exit reads as "game closed". Steam pauses downloads
/// during gameplay by default, so an in-app session meant the queued updates only
/// began once the whole app closed (observed live). Hosting the session in this
/// short-lived child lets the client start downloading seconds later while the app
/// stays open.
/// </summary>
/// <remarks>
/// <para><b>This used to unsubscribe and resubscribe, and no longer does.</b> That
/// sequence was adopted from RimSort on the strength of one observation — "the owner
/// pressed Update and nothing happened" — which was later shown to have been taken
/// under two independent defects, both fixed since and neither retested. The session
/// ran <i>in-process</i>, so Steam had downloads paused for as long as the app lived;
/// and the UGC interface was acquired by version string, handing the game's
/// v016-compiled flat functions a v022 vtable to index, so the <c>DownloadItem</c>
/// under test was dispatched into some other method entirely and never ran. Either
/// alone was sufficient to produce the null result that condemned it.</para>
/// <para><b>Retested properly, a bare <c>DownloadItem</c> works</b> (9 Aug 2026, two
/// trials against the real install, both against a genuinely stale subscribed item).
/// The client returned true, moved the item to <c>DownloadPending</c> inside the
/// session, and completed the download after this process exited: the acf's manifest
/// GID changed, <c>timeupdated</c> advanced to exactly the remote publish time, and
/// the bytes on disk changed. The control that rules out a coincidental Steam poll is
/// that the other thirteen stale mods were untouched. Trial 2 also caught the
/// mechanism: <c>DownloadItem</c> <i>set</i> <c>k_EItemStateNeedsUpdate</c> itself, so
/// the client does not need to already know an item is stale — which matters, because
/// it never does. Steam's own <c>latest_timeupdated</c> agreed with the installed
/// version for all 559 items while fourteen had updates published.</para>
/// <para>Dropping the resubscribe also removes a hazard rather than trading one:
/// there is no longer a window in which the user is unsubscribed from their own mods,
/// so cancellation no longer has to be ignored to keep them safe.</para>
/// </remarks>
public static class SteamworksDownload
{
    public const string ArgumentMarker = "--steamworks-download";

    public const int Ok = 0;
    public const int BadArguments = 64;
    public const int NoSteamApiLibrary = 65;
    public const int ClientUnreachable = 66;

    /// <summary><c>DownloadItem</c> returned false: the client refused the request
    /// outright — an item it does not know, or one the user is not subscribed to —
    /// rather than accepting it. Distinct from "accepted and nothing happened",
    /// because those need different answers.</summary>
    public const int DownloadRefused = 67;

    /// <summary>
    /// Args after the marker: game dir, app id, then one or more item ids.
    /// <b>Fully synchronous on one thread, deliberately</b> — every Steamworks call
    /// comes from the thread that ran <c>Init</c>. Blocking is fine here; this
    /// process exists only for these seconds.
    /// <para>Reports per item on stdout as <c>key=value</c> lines, the convention
    /// <see cref="SteamworksCollectionCreate"/> already uses. The child that could
    /// say nothing is how a misdispatched call went unnoticed through two commits.</para>
    /// </summary>
    public static int Run(IReadOnlyList<string> args)
    {
        if (args.Count < 3 || !int.TryParse(args[1], out var appId)) return BadArguments;
        var gameDirectory = args[0];

        var ids = new List<ulong>();
        foreach (var raw in args.Skip(2))
        {
            if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return BadArguments;
            ids.Add(id);
        }

        var library = FindSteamApiLibrary(gameDirectory);
        if (library is null) return NoSteamApiLibrary;

        var api = SteamApi.Load(library);
        if (!api.Init(appId)) return ClientUnreachable;

        var refused = false;
        try
        {
            var ugc = api.GetUgc();

            // NO SteamAPI_RunCallbacks anywhere, found by access violation: the
            // dispatch needs the SDK's C++ callback-manager state to deliver into,
            // which a flat P/Invoke binding does not have. The queued operations
            // execute client-side without any dispatch.
            foreach (var id in ids)
            {
                var before = api.GetItemState(ugc, id);
                var accepted = api.DownloadItem(ugc, id, highPriority: true);
                if (!accepted) refused = true;

                Console.Out.WriteLine($"item={id}");
                Console.Out.WriteLine($"state-before={before} ({DescribeItemState(before)})");
                Console.Out.WriteLine($"accepted={(accepted ? 1 : 0)}");
            }

            // Not a stage wait — there is no second stage to order against. It is
            // here so the request reaches the client before the process dies; a
            // child exiting in 200ms would race its own IPC.
            Thread.Sleep(1000);

            // A Downloading or DownloadPending bit here is the client saying it took
            // the work, and it is the only confirmation available from inside. Its
            // absence proves nothing on its own: downloads stay paused while this
            // process lives, which is the whole reason the child exists.
            foreach (var id in ids)
            {
                var after = api.GetItemState(ugc, id);
                Console.Out.WriteLine($"state-after={id}:{after} ({DescribeItemState(after)})");
            }
        }
        finally
        {
            api.Shutdown();
        }

        return refused ? DownloadRefused : Ok;
    }

    /// <summary>
    /// The <c>EItemState</c> bit field, spelled out because this subsystem's history
    /// is of guessing at client state instead of reading it.
    /// </summary>
    internal static string DescribeItemState(uint state)
    {
        if (state == uint.MaxValue) return "not measured";
        if (state == 0) return "None";

        var names = new List<string>();
        if ((state & 1) != 0) names.Add("Subscribed");
        if ((state & 2) != 0) names.Add("LegacyItem");
        if ((state & 4) != 0) names.Add("Installed");
        if ((state & 8) != 0) names.Add("NeedsUpdate");
        if ((state & 16) != 0) names.Add("Downloading");
        if ((state & 32) != 0) names.Add("DownloadPending");

        const uint known = 1u | 2u | 4u | 8u | 16u | 32u;
        if ((state & ~known) != 0) names.Add($"unknown:0x{state & ~known:x}");

        return string.Join("|", names);
    }

    /// <summary>
    /// The game dir is searched rather than a path assumed, because Unity has moved
    /// its plugin folder between versions (<c>Plugins/</c> vs <c>Plugins/x86_64/</c>).
    /// </summary>
    private static string? FindSteamApiLibrary(string gameDirectory)
    {
        var name = OperatingSystem.IsWindows() ? "steam_api64.dll"
            : OperatingSystem.IsMacOS() ? "libsteam_api.dylib"
            : "libsteam_api.so";

        try
        {
            return Directory.EnumerateFiles(gameDirectory, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The flat-API binding, a handful of calls wide, loaded from <b>the game's own
    /// Steamworks library</b> — no NuGet wrapper (all stale or third-party repacks),
    /// nothing redistributed, SDK version matched to the game by construction. The
    /// exports were verified against the shipped dll's PE export table; the UGC
    /// accessor is versioned per SDK (RimWorld 1.6 ships <c>v016</c>), so it is
    /// probed newest-first rather than assumed.
    /// </summary>
    private sealed class SteamApi
    {
        private static SteamApi? _loaded;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool InitFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AccessorFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool DownloadItemFn(IntPtr self, ulong id, [MarshalAs(UnmanagedType.I1)] bool highPriority);

        /// <summary>The <c>EItemState</c> bit field — see <see cref="DescribeItemState"/>.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetItemStateFn(IntPtr self, ulong id);

        private readonly IntPtr _lib;
        private readonly InitFn _init;
        private readonly VoidFn _shutdown;
        private readonly DownloadItemFn _downloadItem;
        private readonly GetItemStateFn? _getItemState;

        private SteamApi(IntPtr lib)
        {
            _lib = lib;
            _init = Bind<InitFn>("SteamAPI_Init");
            _shutdown = Bind<VoidFn>("SteamAPI_Shutdown");
            _downloadItem = Bind<DownloadItemFn>("SteamAPI_ISteamUGC_DownloadItem");

            // OPTIONAL, deliberately: Bind throws on a missing export and nothing
            // here catches it, so a diagnostic must never be able to take the
            // download down with it.
            _getItemState = TryBind<GetItemStateFn>("SteamAPI_ISteamUGC_GetItemState");
        }

        public static SteamApi Load(string path) => _loaded ??= new(NativeLibrary.Load(path));

        private T Bind<T>(string export) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, export));

        /// <summary>Null rather than throwing, for exports a path can do without.</summary>
        private T? TryBind<T>(string export) where T : Delegate =>
            NativeLibrary.TryGetExport(_lib, export, out var address)
                ? Marshal.GetDelegateForFunctionPointer<T>(address)
                : null;

        /// <summary>Launched outside Steam, the SDK learns the app id from the
        /// environment; both names, because the SDK has read either over the years.</summary>
        public bool Init(int appId)
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(CultureInfo.InvariantCulture));
            return _init();
        }

        public void Shutdown() => _shutdown();

        public IntPtr GetUgc()
        {
            // ONLY the dll's own versioned accessor may pick the interface. The flat
            // functions are compiled against exactly one vtable layout — the dll's
            // own — and asking the client for any other version by string
            // (SteamInternal_FindOrCreateUserInterface) hands back a REAL object of
            // the WRONG version: the modern steamclient serves v022 happily, the
            // game's v016 flat functions then index shifted vtable slots. That bug
            // cost this project two commits and a false finding — it is what made a
            // working DownloadItem look inert. Probing newest-first is still right —
            // a game update that bumps the SDK renames the accessor — but a missing
            // accessor must mean "try the next name", never "ask the client by string".
            for (var version = 22; version >= 14; version--)
            {
                if (NativeLibrary.TryGetExport(_lib, $"SteamAPI_SteamUGC_v{version:000}", out var accessor))
                {
                    var ugc = Marshal.GetDelegateForFunctionPointer<AccessorFn>(accessor)();
                    if (ugc != IntPtr.Zero) return ugc;
                }
            }

            throw new InvalidOperationException("The game's Steamworks library offered no UGC interface.");
        }

        public bool DownloadItem(IntPtr ugc, ulong id, bool highPriority) => _downloadItem(ugc, id, highPriority);

        /// <summary>uint.MaxValue when the export is absent — never a plausible bit
        /// field, so a reader cannot mistake "not measured" for a real state.</summary>
        public uint GetItemState(IntPtr ugc, ulong id) =>
            _getItemState is null ? uint.MaxValue : _getItemState(ugc, id);
    }
}
