<div align="center">

<img src="assets/brand/rimmanager-256.png" width="120" alt="">

# RimManager

A desktop mod manager for **RimWorld**, heavily inspired by [RimSort](https://github.com/RimSort/RimSort).<br>
Deterministic load-order sorting, assembly-level conflict detection and shareable modlists.<br>
Windows, macOS and Linux.

**[Releases](https://github.com/SalehBusbait/RimManager/releases) | [Issues](https://github.com/SalehBusbait/RimManager/issues) | [Support](#support)**

<a href="https://github.com/SalehBusbait/RimManager/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/SalehBusbait/RimManager?include_prereleases&style=flat&label=release&color=4c8dd9"></a>
<a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/badge/licence-MIT-4c8dd9?style=flat"></a>
<img alt="Windows, macOS and Linux" src="https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-8b93a1?style=flat">

</div>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/screenshots/main-window-dark.png">
  <img alt="The main window: inactive mods on the left, the active load order with separators and coloured tags in the centre, mod details on the right, and the updates dock below" src="assets/screenshots/main-window-light.png" width="100%">
</picture>

> [!NOTE]
> This is a pre-release. Version `1.0.0-beta.1` is in daily use against a 565-mod
> installation and the pipeline carries 1,469 tests, but it has had one reviewer.
> Defect reports are welcome.

## Features

- **Load-order sorting.** A rule graph is assembled from each mod's `About.xml` and the community rules database, then sorted by a deterministic, idempotent topological pass. A genuine contradiction is preserved as a cycle rather than silently collapsed: the sort names the loop and the single rule it dropped in order to complete, and that decision can be pinned to the modlist so every subsequent sort resolves it identically.

- **Conflict detection at the assembly level.** RimManager reads the compiled assemblies of every mod in the active load order with a metadata reader — mod code is never executed or loaded — and reports which mods declare Harmony patches against the same method. Def overrides, XML patch operations and texture replacements are resolved in the same pass, with the winner determined from the current order rather than from the scan.

- **Validation before the game runs.** Missing and inactive dependencies, absent DLC, mods declared incompatible with one another, load-order violations and unsupported game versions are reported as a categorised list. A resolver dialog offers the corresponding fix for each dependency.

- **Modlists with append-only history.** Load orders are switched as named modlists, each able to retain its own mod settings. Applying and sorting record snapshots that can be named and returned to; restoring appends a new state rather than rewinding an existing one. Hand edits are undoable.

- **Drift detection.** When RimWorld or another tool alters the load order externally, RimManager reports the divergence and offers to review it as a diff, adopt it as a new modlist, or replace the current one.

- **Organisation.** Separators group the load order into collapsible sections; tags are colour-coded from a palette index rather than a fixed hex value, so they follow the active theme. Both can be filtered on.

- **Workshop integration.** Update detection compares the installed publish time recorded in Steam's own manifest against the live value, so it is exact rather than inferred from file timestamps. Downloads are requested from the Steam client, which leaves subscriptions untouched.

- **Sharing.** A list can be exported as a `.rwlist` document, as a Workshop item folder ready to upload, or as a private Steam collection created on the user's own account. All three import, as does a collection URL.

- **A single file written.** RimManager writes only `ModsConfig.xml`, reproducing RimWorld's format byte for byte, after a timestamped backup, and declines to write while the game is running.

## Installation

Download the archive for the target platform from the
[releases page](https://github.com/SalehBusbait/RimManager/releases) and extract it. The
.NET runtime is included, so no separate installation or launcher is required.

| Platform | Archive | Notes |
|---|---|---|
| Windows | `win-x64` (`.zip`) | Run `RimManager.exe`. SmartScreen will report an unsigned application; choose **More info**, then **Run anyway**. |
| macOS | `osx-arm64` or `osx-x64` (`.tar.gz`) | Run `./RimManager`. Gatekeeper blocks unsigned binaries on first launch — permit it under **System Settings ▸ Privacy & Security**, or run `xattr -dr com.apple.quarantine RimManager`. |
| Linux | `linux-x64` (`.tar.gz`) | Run `./RimManager`. |

Archives named `RimManager-cli-*` contain the command-line binary, which exposes the same
capabilities for scripted use.

On first launch the RimWorld installation is located through Steam's library manifest. GOG
and manual installations can be selected in Settings.

## Screenshots

Per-mod conflict attribution: the Defs and Harmony methods a mod contends for, and which mod prevails.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/screenshots/conflicts-dark.png">
  <img alt="The conflicts window for one mod, listing the Defs it overwrites and is overwritten by, with the mod responsible for each" src="assets/screenshots/conflicts-light.png" width="100%">
</picture>

Ten themes, with light and dark treated as equals; each carries its own accent colour.

<img alt="The appearance settings page showing ten selectable themes as preview tiles" src="assets/screenshots/themes.png" width="100%">

Modlists are the unit of switching, and each may retain its own mod settings.

<img alt="The modlists settings page listing two modlists with their mod counts, snapshots and last-used times" src="assets/screenshots/modlists.png" width="100%">

<details>
<summary>Building from source</summary>

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download), pinned in `global.json`.

```
dotnet build RimManager.slnx -c Release
dotnet test  RimManager.slnx -c Release
dotnet run --project src/RimManager.App
```

The solution file is `RimManager.slnx`, the XML solution format. Warnings are treated as
errors in Release configuration. Tests requiring network access skip cleanly when offline.

</details>

## Acknowledgements

RimManager was heavily inspired by [RimSort](https://github.com/RimSort/RimSort), the mod
manager maintained by the RimWorld community. Readers seeking a mature tool with an
established user base should use it.

Sorting draws on the [Community Rules Database](https://github.com/RimSort/Community-Rules-Database),
curated and published by the RimSort project. Mod replacement and version-compatibility
data are provided by Mlie's [UseThisInstead](https://github.com/emipa606/UseThisInstead)
and [NoVersionWarning](https://github.com/emipa606/NoVersionWarning). All three are
retrieved at runtime; RimManager neither modifies nor redistributes them.

RimManager is an independent project. It is not affiliated with, endorsed by, or connected
to Ludeon Studios. RimWorld is a trademark of Ludeon Studios.

## Licence

RimManager is distributed under the [MIT Licence](LICENSE), copyright © 2026 Saleh Busubait.
Bundled third-party components retain their own licences; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Support

RimManager is free software and will remain so. No feature is reserved for donors, and no
capability is withheld behind a subscription.

Contributions toward continued development are welcome but entirely optional.

<div align="center">

<a href="https://ko-fi.com/salehbusubait"><img alt="Support RimManager on Ko-fi" src="https://img.shields.io/badge/Ko--fi-Support%20development-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white"></a>

</div>
