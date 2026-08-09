using System.Globalization;
using System.Runtime.InteropServices;

namespace RimManager.Integrations.Steamworks;

/// <summary>
/// The child-process half of "Export as Steam collection" (NF-10 slice 4): creates a
/// <b>private</b> collection on the user's own Workshop account — CreateItem with the
/// collection file type, title + private visibility via an item update, then one
/// AddDependency per mod — prints <c>collection=&lt;id&gt;</c> to stdout and exits.
/// Same child-process rules as <see cref="SteamworksResubscribe"/>: the session reads
/// as "RimWorld is running" until the process exits, so it lives here and dies fast.
/// <para>
/// <b>Call results without RunCallbacks.</b> CreateItem/SubmitItemUpdate answer via
/// SteamAPICall_t handles, and the resubscribe lesson stands: <c>RunCallbacks</c>
/// from a flat P/Invoke binding corrupts the session (its dispatch needs the SDK's
/// C++ callback-manager state). The SDK's own answer for bindings like this one is
/// <b>polling</b>: <c>ISteamUtils::IsAPICallCompleted</c> then
/// <c>GetAPICallResult</c> into a raw buffer — no dispatch, no C++ state, parsed
/// with BitConverter rather than marshalled structs so no layout attribute can lie.
/// </para>
/// <para>
/// Private on purpose: the export must never publish anything on its own. The user
/// reviews the collection on its page and makes it public there — or deletes it,
/// which is also where Steam's own tools for that live.
/// </para>
/// </summary>
public static class SteamworksCollectionCreate
{
    public const string ArgumentMarker = "--steamworks-create-collection";

    public const int Ok = 0;
    public const int BadArguments = 64;
    public const int NoSteamApiLibrary = 65;
    public const int ClientUnreachable = 66;
    public const int MissingExport = 67;
    public const int CreateFailed = 68;
    public const int SubmitFailed = 69;
    public const int TimedOut = 70;

    private const int EResultOk = 1;
    private const int CreateItemResultCallbackId = 3403;       // k_iSteamUGCCallbacks + 3
    private const int SubmitItemUpdateResultCallbackId = 3404; // k_iSteamUGCCallbacks + 4
    private const int CollectionFileType = 2;                  // k_EWorkshopFileTypeCollection
    private const int VisibilityPrivate = 2;                   // ...FileVisibilityPrivate

    /// <summary>Args after the marker: game dir, app id, title, then one or more
    /// Workshop item ids. Synchronous on one thread, like the resubscribe.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        if (args.Count < 4 || !int.TryParse(args[1], out var appId)) return BadArguments;
        var gameDirectory = args[0];
        var title = args[2];

        var children = new List<ulong>();
        foreach (var raw in args.Skip(3))
        {
            if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return BadArguments;
            children.Add(id);
        }

        var library = FindSteamApiLibrary(gameDirectory);
        if (library is null) return NoSteamApiLibrary;

        Api api;
        try
        {
            api = new Api(NativeLibrary.Load(library));
        }
        catch (EntryPointNotFoundException ex)
        {
            Console.Out.WriteLine($"missing-export={ex.Message}");
            return MissingExport;
        }

        if (!api.Init(appId)) return ClientUnreachable;
        try
        {
            var ugc = api.GetInterface("SteamAPI_SteamUGC_v", 22, 14);
            var utils = api.GetInterface("SteamAPI_SteamUtils_v", 11, 8);

            // --- create -----------------------------------------------------
            var createCall = api.CreateItem(ugc, (uint)appId, CollectionFileType);
            if (!api.AwaitCallResult(utils, createCall, CreateItemResultCallbackId,
                    out var createResult))
                return TimedOut;

            var eResult = BitConverter.ToInt32(createResult, 0);
            if (eResult != EResultOk)
            {
                Console.Out.WriteLine($"eresult={eResult}");
                return CreateFailed;
            }

            var collectionId = BitConverter.ToUInt64(createResult, 8);
            var needsLegalAgreement = createResult[16] != 0;

            // --- title + private visibility ---------------------------------
            var update = api.StartItemUpdate(ugc, (uint)appId, collectionId);
            api.SetItemTitle(ugc, update, title);
            api.SetItemVisibility(ugc, update, VisibilityPrivate);
            var submitCall = api.SubmitItemUpdate(ugc, update, null);
            if (!api.AwaitCallResult(utils, submitCall, SubmitItemUpdateResultCallbackId,
                    out var submitResult))
                return TimedOut;

            eResult = BitConverter.ToInt32(submitResult, 0);
            if (eResult != EResultOk)
            {
                Console.Out.WriteLine($"collection={collectionId}");
                Console.Out.WriteLine($"eresult={eResult}");
                return SubmitFailed;
            }

            // --- members ----------------------------------------------------
            // Completion is awaited per call (the client processes these server-side);
            // a failed member is reported but does not undo the collection.
            var added = 0;
            foreach (var child in children)
            {
                var call = api.AddDependency(ugc, collectionId, child);
                if (api.AwaitCallResult(utils, call, expectedCallbackId: null, out _)) added++;
                Thread.Sleep(50);
            }

            Console.Out.WriteLine($"collection={collectionId}");
            Console.Out.WriteLine($"added={added}/{children.Count}");
            if (needsLegalAgreement) Console.Out.WriteLine("legal-agreement=pending");
            return Ok;
        }
        finally
        {
            api.Shutdown();
        }
    }

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
    /// The flat binding, same rules as the resubscribe's paid for in access
    /// violations: only the dll's own versioned accessor picks an interface, and
    /// nothing here ever calls <c>SteamAPI_RunCallbacks</c>.
    /// </summary>
    private sealed class Api
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool InitFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AccessorFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong CreateItemFn(IntPtr self, uint appId, int fileType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong StartItemUpdateFn(IntPtr self, uint appId, ulong fileId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SetItemTitleFn(IntPtr self, ulong handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SetItemVisibilityFn(IntPtr self, ulong handle, int visibility);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong SubmitItemUpdateFn(IntPtr self, ulong handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? changeNote);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong AddDependencyFn(IntPtr self, ulong parent, ulong child);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool IsAPICallCompletedFn(IntPtr self, ulong call,
            [MarshalAs(UnmanagedType.I1)] out bool failed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool GetAPICallResultFn(IntPtr self, ulong call,
            IntPtr buffer, int bufferSize, int expectedCallbackId,
            [MarshalAs(UnmanagedType.I1)] out bool failed);

        private readonly IntPtr _lib;
        private readonly InitFn _init;
        private readonly VoidFn _shutdown;
        private readonly CreateItemFn _createItem;
        private readonly StartItemUpdateFn _startItemUpdate;
        private readonly SetItemTitleFn _setItemTitle;
        private readonly SetItemVisibilityFn _setItemVisibility;
        private readonly SubmitItemUpdateFn _submitItemUpdate;
        private readonly AddDependencyFn _addDependency;
        private readonly IsAPICallCompletedFn _isCompleted;
        private readonly GetAPICallResultFn _getResult;

        public Api(IntPtr lib)
        {
            _lib = lib;
            _init = Bind<InitFn>("SteamAPI_Init");
            _shutdown = Bind<VoidFn>("SteamAPI_Shutdown");
            _createItem = Bind<CreateItemFn>("SteamAPI_ISteamUGC_CreateItem");
            _startItemUpdate = Bind<StartItemUpdateFn>("SteamAPI_ISteamUGC_StartItemUpdate");
            _setItemTitle = Bind<SetItemTitleFn>("SteamAPI_ISteamUGC_SetItemTitle");
            _setItemVisibility = Bind<SetItemVisibilityFn>("SteamAPI_ISteamUGC_SetItemVisibility");
            _submitItemUpdate = Bind<SubmitItemUpdateFn>("SteamAPI_ISteamUGC_SubmitItemUpdate");
            _addDependency = Bind<AddDependencyFn>("SteamAPI_ISteamUGC_AddDependency");
            _isCompleted = Bind<IsAPICallCompletedFn>("SteamAPI_ISteamUtils_IsAPICallCompleted");
            _getResult = Bind<GetAPICallResultFn>("SteamAPI_ISteamUtils_GetAPICallResult");
        }

        private T Bind<T>(string export) where T : Delegate =>
            NativeLibrary.TryGetExport(_lib, export, out var address)
                ? Marshal.GetDelegateForFunctionPointer<T>(address)
                : throw new EntryPointNotFoundException(export);

        public bool Init(int appId)
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(CultureInfo.InvariantCulture));
            return _init();
        }

        public void Shutdown() => _shutdown();

        /// <summary>Versioned-accessor probing, newest first — the resubscribe's rule:
        /// a missing accessor means "try the next name", never "ask the client by string".</summary>
        public IntPtr GetInterface(string prefix, int newest, int oldest)
        {
            for (var version = newest; version >= oldest; version--)
            {
                if (NativeLibrary.TryGetExport(_lib, $"{prefix}{version:000}", out var accessor))
                {
                    var iface = Marshal.GetDelegateForFunctionPointer<AccessorFn>(accessor)();
                    if (iface != IntPtr.Zero) return iface;
                }
            }

            throw new EntryPointNotFoundException(prefix + "NNN");
        }

        public ulong CreateItem(IntPtr ugc, uint appId, int fileType) => _createItem(ugc, appId, fileType);
        public ulong StartItemUpdate(IntPtr ugc, uint appId, ulong fileId) => _startItemUpdate(ugc, appId, fileId);
        public void SetItemTitle(IntPtr ugc, ulong handle, string title) => _setItemTitle(ugc, handle, title);
        public void SetItemVisibility(IntPtr ugc, ulong handle, int visibility) => _setItemVisibility(ugc, handle, visibility);
        public ulong SubmitItemUpdate(IntPtr ugc, ulong handle, string? note) => _submitItemUpdate(ugc, handle, note);
        public ulong AddDependency(IntPtr ugc, ulong parent, ulong child) => _addDependency(ugc, parent, child);

        /// <summary>
        /// Polls a SteamAPICall_t to completion (100ms · 30s budget), then copies the
        /// result payload into <paramref name="payload"/>. A null
        /// <paramref name="expectedCallbackId"/> awaits completion only — used where
        /// the payload does not matter and a wrong expected id would read as failure.
        /// </summary>
        public bool AwaitCallResult(IntPtr utils, ulong call, int? expectedCallbackId, out byte[] payload)
        {
            payload = new byte[64];

            for (var waited = 0; waited < 30_000; waited += 100)
            {
                if (_isCompleted(utils, call, out _))
                {
                    if (expectedCallbackId is not { } id) return true;

                    var buffer = Marshal.AllocHGlobal(payload.Length);
                    try
                    {
                        var ok = _getResult(utils, call, buffer, payload.Length, id, out var failed);
                        if (!ok || failed) return false;
                        Marshal.Copy(buffer, payload, 0, payload.Length);
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }

                Thread.Sleep(100);
            }

            return false;
        }
    }
}
