# Updates and the Workshop

The Updates tab in the bottom dock tells you which of your installed Workshop mods
have a newer version on Steam, and lets you update the ones you choose. Nothing here
touches your game files: checking only reads, and the downloading itself is always
done by Steam or SteamCMD, never by RimManager writing into a mod folder it manages.

## Checking for updates

Run Tools ▸ Check for mod updates (Ctrl+U), or press Check for updates on the tab
itself. Once a result is on screen the toolbar button reads Check again.

A check asks Steam for each installed mod's publish time and compares it with what
is on your disk. It is exact — the answer comes from Steam, not a guess — and it
downloads nothing by itself. The summary line on the right states the totals, for
example "7 updates · 3 delisted · 189 up to date · 2 untracked", or "All up to
date" when there is nothing to do.

The table is a worklist, not an inventory. Only mods that need a decision get a
row: available updates first, then delisted mods, then snoozed ones. The hundreds
that are simply up to date are counted in the summary and stay out of your way.

If a check cannot reach Steam, the previous result stays on screen with a small
"stale" badge — the last answer is still the best one available, it is just no
longer known to be current.

## Reading the table

The columns are SRC (where the mod comes from), MOD, INSTALLED, PUBLISHED, SIZE and
STATE. PUBLISHED is relative — "2 days ago", "yesterday" — with the exact time in
the tooltip. Steam publishes an update time, never a version number, so recency is
the real signal. The chips on the left (All, Update, Snoozed, Delisted) filter the
table by state without changing any of the counts.

A state of "delisted" means the mod is no longer on the Workshop. Your local copy
is safe, and RimManager will keep loading it — but unsubscribing in Steam would
delete it, so the tab flags it rather than letting that happen by surprise.

Selecting a row fills the panel on the right. Steam does not publish changelogs in
a form an app can read, so instead of inventing one the panel says so and offers
the Workshop button, which opens the mod's page — where the author's own change
notes live.

## Updating mods

Tick the rows you want and press Update N selected, or use Update this in the
detail panel for a single mod. The checkbox in the table header selects the safe
set in one click: it never ticks a pre-release version or a mod with local edits.
Those rows can still be updated, but you have to tick them deliberately.

A confirmation states exactly what will happen before anything does. The update
goes through the Steam client: Steam downloads today's version of each mod and
keeps its own bookkeeping. Your subscriptions are not touched, nothing is copied
into your Mods folder, and mod settings and saves are untouched. Steam may show
you as in-game for a few seconds while the request is made.

One thing cannot be undone: the Workshop serves only the latest version of a mod,
so there is no rolling back once Steam has updated it. That is why this tab asks
which mods to update instead of updating everything automatically. Your load-order
position, tags and notes are keyed to the mod's identity and survive the update,
and a snapshot of your list is taken before the batch runs — see
[modlists and history](modlists-and-history.md).

When the batch finishes, RimManager rescans and checks again, and each updated mod
simply leaves the worklist.

## Snoozing an update

Not every update is one you want today. Select a row and open Snooze ▾ to quiet it
For 1 week, Until the next version, or Until the next game version — the last is
the usual choice when you are holding a mod back for your current playthrough.

Snoozes survive restarts and expire on their own terms: a week passing, the mod
publishing a new version, or the game moving to a new version. A snoozed mod stays
visible under the Snoozed chip and stops counting toward the number on the tab.
Un-snooze it any time from the same menu or the Un-snooze button in the detail
panel.

## Getting mods you do not have yet

The Updates tab only updates what is already installed. When RimManager needs to
fetch something new — importing a Workshop collection, or resolving a missing
dependency — it offers two routes and lets you choose.

Subscribe in Steam hands the job to the Steam client. For a collection it opens
the collection page, where Steam's own Subscribe to all fetches everything and
keeps it updated afterwards. This is the route to prefer when Steam is running:
the mods stay Steam-managed.

Download via SteamCMD is the opt-in alternative for mods you do not want to
subscribe to, or when Steam is closed. It uses Valve's SteamCMD anonymously — no
account and no login — and the first use downloads SteamCMD itself (about 200 MB)
into RimManager's own folder. Mods fetched this way land in your Mods folder
unmanaged, so Steam does not update them; they show up in this tab like anything
else, and only RimManager tracks their updates.

See [sharing](sharing.md) for importing collections and lists, and the
[FAQ](faq.md) for what RimManager does and does not write — the short version is
that the only game file it ever touches is ModsConfig.xml, backed up first, and
never while RimWorld is running.
