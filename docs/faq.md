# Frequently asked questions

Short answers to the questions that come up most. For how ordering works, see
[sorting](sorting.md).

## Is my game safe?

Yes. RimManager writes exactly one game file: `ModsConfig.xml`, the small file
RimWorld reads at startup to know which mods to load and in what order. Before
every write it makes a timestamped backup, and it refuses to write at all while
RimWorld is running — you will see "RimWorld is running — refusing to write
ModsConfig.xml. Close the game and retry." It never touches your saves, your mod
files, or anything else in the game folder.

If a modded game ever misbehaves, the backups are right there, and the worst
case is restoring one small text file.

## Where is my data?

Everything RimManager keeps — modlists, tags, snapshot history, settings — lives
in one folder: `%LocalAppData%\RimManager` on Windows, or the equivalent
per-user application data folder on macOS and Linux. The files are plain JSON.
You can open them in any text editor, and you can back up the whole folder by
copying it.

## How do I remove everything?

On Windows, uninstall from Apps & Features. The uninstaller asks "Also remove
your RimManager data?" — answer Yes to delete the data folder too, or No to keep
your modlists for a future install. If you used the portable version, delete the
app folder and `%LocalAppData%\RimManager` yourself. Your game, saves and mods
are not affected either way.

## Why does Windows or macOS warn me on first launch?

RimManager is not code-signed, so the operating system cannot vouch for who
built it. On Windows, SmartScreen shows a warning: choose "More info", then
"Run anyway". On macOS, Gatekeeper blocks the first launch: allow it under
System Settings, Privacy & Security. This is a one-time hurdle, not a sign that
anything is wrong with the download.

## Does it work with GOG?

Yes. GOG and DRM-free installs are found automatically and are first-class —
scanning, sorting, modlists and applying all work the same. Only the features
that talk to Steam itself, such as Workshop update checking and downloads, need
a Steam install and the Steam client.

## What does "delisted" mean?

A mod you have installed is no longer available on the Steam Workshop — the
author removed it, or Steam did. Your local copy keeps working exactly as it is;
it just cannot receive updates, and anyone importing your list will not be able
to download it. The Updates tab has a "Delisted" filter so you can see these at
a glance.

## Why does it say my order changed outside the app?

The status bar shows "Changed outside · Review" when something other than
RimManager wrote `ModsConfig.xml` — usually RimWorld itself, because you changed
the mod list in-game or enabled a DLC. Nothing is lost: RimManager notices the
difference and offers "Review differences…" so you can decide whether to keep
your list or take the game's. This state is called drift, and applying your list
clears it.

## Can I undo a Workshop update?

No, and it helps to know why. The History tab snapshots your load order — which
mods were active and in what order — not the mods' files. When Steam updates a
Workshop mod it replaces the files in place and does not keep the old version,
so there is nothing on disk to go back to. "Restore this state" brings back an
earlier order; it cannot bring back an earlier version of a mod.

## Where do bug reports go?

To the GitHub issue tracker — "Report an issue" in the Help menu takes you
there. Two things make a report much easier to act on:

- the version line: open Help, "About RimManager" and press "Copy version info"
- the log: open the Activity tab and press "Copy all", or use "Copy diagnostics
  bundle" in the Help menu

Paste both into the issue along with what you did and what you expected.
