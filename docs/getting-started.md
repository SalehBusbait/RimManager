# Getting started

RimManager is a mod manager for RimWorld that runs the same on Windows, Linux and
macOS. This page takes you from download to launching the game with a managed load
order. Along the way it only ever writes one file in your game's configuration —
`ModsConfig.xml`, the same file RimWorld's own mod screen writes — and it backs that
file up first, every time.

## Installing

Every release on the [releases page](https://github.com/SalehBusbait/RimManager/releases)
ships the same app in a few shapes. Pick the one that suits you:

- **Windows installer** — `RimManager-Setup-<version>.exe`. Installs like any other
  program, registers RimManager in Apps & Features, and gives you a proper
  uninstaller.
- **Windows portable** — `RimManager-<version>-win-x64.zip`. Unzip anywhere and run
  `RimManager.exe`. Nothing is installed; delete the folder and it is gone.
- **Linux** — `RimManager-<version>-linux-x64.tar.gz`. Extract and run the
  `RimManager` executable inside.
- **macOS** — `RimManager-<version>-osx-arm64.tar.gz` for Apple Silicon,
  `osx-x64` for Intel Macs. Extract and run `RimManager`.

The first time you run it, your operating system will probably object. RimManager is
an open-source project without a paid signing certificate, so Windows SmartScreen
shows "Windows protected your PC" — click **More info**, then **Run anyway**. On
macOS, Gatekeeper refuses at first: right-click the app, choose **Open**, and confirm,
or allow it under System Settings, Privacy & Security. This happens once.

## First launch

On first launch a short setup wizard opens, titled "Set up RimManager". It has four
steps — Welcome, Paths, Modlist, Rules — and takes about three minutes. Nothing is
written to your game folder during setup, and you can leave at any point with
**Skip setup**. To run it again later, use **Help ▸ Re-run first-time setup…**.

The Paths step is the one that matters. If you own RimWorld on Steam, the wizard
usually finds it on its own and the **Steam install** card shows "detected" with the
folder it found. If you have a GOG or other DRM-free copy, pick **Other install** and
point the two fields — **Game install folder** and **Config folder** — at the right
places yourself. Workshop features stay off for a non-Steam install; local mods work
exactly the same. Everything here can be changed later in Settings, on the Paths page.

The Modlist step reads your current setup: your active mods, in the order the game is
using right now, become your first modlist, and that starting point is kept as
snapshot #1 so anything you do later can be compared against it or rolled back. Give
the list a name, and choose whether to group it with separators by load tier. "Sort
the imported order immediately" is off by default on purpose — your existing order
works today, so sort when you choose to.

The Rules step offers two optional extras: **Download the community rules database**
(load-order knowledge maintained by the RimSort community, refreshed weekly) and
**Check for mod updates on startup**. Both are safe to say yes to, and both live in
Settings afterwards. Click **Open RimManager** and you are in.

## The main window

The window is three panes, left to right:

- **Inactive** — every installed mod that is not in your load order, with columns for
  source, name and more. This is your shelf.
- **Active load order** — the mods RimWorld will load, top to bottom, plus any
  separators you use to group them. Drag mods between the panes, or within this one
  to reorder.
- **Mod info** — details for whichever mod is selected: description, version,
  dependencies, warnings, and your own tags and notes.

Counts in each pane header tell you how many mods are where. The search field in the
toolbar narrows both lists as you type.

## Your first sort

Click **Sort** in the toolbar (or Tools ▸ Sort load order). RimManager orders your
active list using each mod's declared dependencies plus the community rules, if you
enabled them. The result is deterministic — sorting twice gives the same answer — and
it is a normal, undoable edit: if you dislike it, press the undo button or Ctrl+Z and
your previous order is back. Nothing has touched the game yet.

The small arrow on the Sort button opens its options, including "Snapshot before
sorting" and a choice between the dependency-aware sort and an alphabetical one. See
[sorting](sorting.md) for how the sort decides what goes where.

## Apply to game

Sorting and rearranging happen inside RimManager. The game only sees your changes
when you click **Apply to game** — the one filled button in the toolbar.

Apply writes your active list to `ModsConfig.xml`, after saving a timestamped backup
of the old one. That file is the only thing in your game installation RimManager ever
writes. If there is something worth pausing over — warnings in the list, for instance
— a bar appears under the toolbar stating what will be written and where the backup
goes, and waits for your confirmation. And if RimWorld is running, Apply refuses
outright: the game would overwrite the file on exit, so close it and apply again.

## Launching RimWorld

The arrow on the Apply button (and the Tools menu) offers **Apply and launch
RimWorld**, which does both in one step, and **Launch without applying**, which
starts the game with whatever order it already has. From here, day-to-day use is a
loop: adjust your list, sort, apply, play.
