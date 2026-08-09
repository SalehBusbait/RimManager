# CLAUDE.md

Guidance for Claude Code working in this repository.

RimManager is a cross-platform desktop mod manager for **RimWorld**: MO2's organisational
model (separators, tags, shareable exports) over a deterministic sorting engine, with
assembly-level conflict detection. Avalonia 12 on .NET 10. Current version
`1.0.0-beta.2`, MIT licensed, published at https://github.com/SalehBusbait/RimManager.

## Commands

The solution file is **`RimManager.slnx`** — the XML solution format, not a `.sln`. Use it
in every `dotnet` command.

```bash
dotnet build RimManager.slnx -c Release      # warnings are errors; must be 0/0
dotnet test  RimManager.slnx -c Release      # 1,477 tests across three projects
dotnet test RimManager.slnx --filter "FullyQualifiedName~ModSorter"
dotnet run --project src/RimManager.App      # the GUI
```

Releases are tag-triggered: `git tag v1.0.0-beta.2 && git push origin v1.0.0-beta.2`
builds four runtime identifiers plus the Windows installer and publishes them. The
workflow fails deliberately if the tag and `<Version>` in `Directory.Build.props`
disagree — and if `docs/releases/v<version>.md` is missing. **Release notes are written
before every release**, in that file, from `docs/releases/TEMPLATE.md` (owner's call,
beta.2: beautified — a Highlights blurb, emoji section headings, bold-lead bullets, a
screenshot when anything visible changed, a Downloads routing line, the compare link).
Player-facing wording throughout; the release job publishes the file verbatim.

Kill leftover instances with `Get-Process -Name RimManager | Stop-Process -Force` before
rebuilding; the exe locks otherwise.

## Architecture

Data flows one direction. The App is a thin shell over a pure core. (A CLI twin
existed until beta.2 and was retired as a product decision — the pattern to keep is
that anything testable lives below the shell, not in it.)

`Locators` (find the install via `libraryfolders.vdf`) → `Scanning.ModScanner` (parse
`About.xml`, detect content, dedupe by packageId with source precedence) → `Sorting`
(build a rule DAG, assign tiers, topological sort) → `Validation` → `Writing.ApplyService`
(guard if running → back up → atomic write) → `Storage` persistence → `Sharing` (`.rwlist`).

| Project | Role |
|---|---|
| `RimManager.Core` | Pure domain. No I/O, no UI. All filesystem access through `IFileSystem`, time through `IClock`, processes through `IProcessRunner`. |
| `RimManager.Storage` | The only place I/O happens. Repositories, `JsonDocumentStore`, the Cecil analyzer, the SQLite scan cache. |
| `RimManager.Integrations` | The network and process edge: `HttpClientFetcher`, SteamCMD, Steamworks. |
| `RimManager.App` | Avalonia. `MainWindowViewModel` is one class across ten partial files by surface, capped at 1,800 lines each by `HubShapeTests`. |

## Non-negotiable conventions

- **`RimManager.Core` performs no I/O and has no UI.** This is what makes the domain
  testable against an in-memory double.
- **JSON is the source of truth** for all user data, written atomically with a timestamped
  backup and validated on load. **SQLite is only a disposable derived cache** — deleting it
  must lose nothing.
- **Central Package Management** (`Directory.Packages.props`); no versions in `.csproj`.
  FluentAssertions is pinned to 7.x because 8.0+ is commercially licensed.
- **`ModsConfig.xml` is written by a hand-rolled byte-exact writer.** RimWorld's format
  (`<?xml version="1.0" ?>`, CRLF, 2/4-space indent, no BOM) cannot be reproduced by
  `XmlWriter`. A fixture test guards it.
- **`packageId` identity is case-insensitive** — always route through `ModId`.
- **Sorting is deterministic and idempotent**, enforced by property tests. Cycles are
  normal: detected and broken deterministically, never thrown.
- **Tiers dominate rules.** `ModSorter` drops a rule edge that would order a later tier
  before an earlier one. `Tier.Top` means *first among mods*, so it sits between `Dlc` and
  `Normal`.
- **No literal `#RRGGBB` outside the theme dictionaries.** Everything is
  `{DynamicResource Rm*Brush}`, enforced by a build-time check.
- **Tag and separator colours persist as a palette index, never a hex string**, so they
  follow the theme.
- **Bump `SqliteModCache.CacheVersion` whenever scan semantics change.** The cache keys on
  `About.xml`'s mtime, which neither a Ludeon file nor a `git clone` ever changes.
- **Commit messages are short, imperative and professional, with no trailers.** Reasoning
  that needs to survive belongs in the tree, not in history — the repository is public
  and its history has been squashed once already.

## Traps that have already cost this project time

Avalonia fails silently. Most of these were found by a user noticing something looked
wrong, not by a test, which is why the guards in `App.Tests` exist.

- **A local value on an element outranks every style setter.** Three separate bugs.
  `MainWindowMarkupTests` fails any element setting a property locally when a bound class
  exists to style it.
- **Fluent sets sizes and state colours as local values on inner template parts.** Size
  with `MaxWidth`; target `Border#PART_LayoutRoot`.
- **A horizontal `StackPanel` gives its children infinite width**, so `TextTrimming` never
  engages and text paints over the next column. Guarded by `LayoutTrapTests`.
- **`ScrollViewer.Padding` is subtracted from the scrollable extent**, making the last band
  unreachable. Gutters go on the content as a `Margin`.
- **Equal specificity means the later style wins.** Prefer `:not(...)` over reordering.
- **Hidden rows have no usable geometry.** `ArrangeCore` is wrapped in `if (IsVisible)`
  with no else, so a filtered-out container keeps its pre-filter rectangle for ever. Never
  infer position or height from one.
- **Filtering hides rows; it never rebuilds the collection.** A drop index from the ListBox
  *is* an index into the underlying collection.
- **Poll directories; do not watch them.** A folder built in place raises one `Created`
  while still empty and then nothing. `ModRootProbe` polls.
- **A source check cannot give a false green from a stale binary, but a runtime test can.**
  When verifying a guard by reintroducing a bug, make sure the break still *compiles* —
  warnings are errors, so a broken build silently reruns the previous binary.
- **Never round-trip a source file through PowerShell.** `Get-Content -Raw` reads BOM-less
  UTF-8 as CP1252 and `Set-Content -Encoding utf8` writes a BOM. The same defaults corrupt
  captured output: `>` redirection writes UTF-16, `|` adds a BOM, and `Select-Object
  -First` upstream of a write truncates the file. Use Python or the Edit tool.
- **A number on screen is a claim** and has to be measured against a real install.

## Steamworks

The Workshop updater runs a bare `DownloadItem(high)` in a **short-lived child process**
(`--steamworks-download`, routed in `Program.Main` before any UI). The child's exit is the
mechanism: a session against app 294100 reads as "RimWorld is running", Steam pauses
downloads during gameplay, and `SteamAPI_Shutdown()` does not clear that — the client
watches the process.

Two binding rules, both paid for in access violations:

1. **Only the dll's own versioned accessor may pick the UGC interface.** The flat functions
   are compiled against exactly one vtable layout; asking the client by version string
   returns a real object of the wrong version with shifted slots.
2. **Never call `SteamAPI_RunCallbacks` from a flat P/Invoke binding.** Its dispatch needs
   the SDK's C++ callback-manager state.

Both are guarded by `ChildProcessRoutingTests`, which also pins that every child marker is
routed before `BuildAvaloniaApp`.

**Never bundle the Community Rules Database.** It carries no licence, so redistribution is
not permitted. Fetching it at runtime is what it is published for. The committed fixture is
hand-written synthetic data and a test pins its size.

## Open defects

- **`HistoryPresenter` carries labels for snapshot reasons nothing emits** — `drag`,
  `activate`, `deactivate`, `separator`, `import`. Only before-sort, apply, two drift
  captures and restore produce snapshots.

(The rule editor's unwired overrides — formerly the first entry here — were wired in
beta.2: `RuleGraphBuilder.Build` and `ModListValidator.Validate` both take a
`RuleOverrides` and every hub call site passes `_ruleOverrides`, guarded by
`RuleSourceParityTests`.)

## Where things live

- `docs/` is for **end-user documentation**. It is currently empty and is to be written.
- `docs/archive/` and `docs/AI planning/` are **git-ignored and local only**. The archive
  holds the design handoffs, phase plans, UI audits and the original spec — the reasoning
  behind most decisions in this codebase, kept but not published. Working plans go in
  `docs/AI planning/`.
- `fixtures/` holds real `About.xml` samples and a live `ModsConfig.xml`; the seven
  integration tests that use them skip cleanly when absent.
- `assets/themes/tokens.css` is a **build input**, not documentation: it is the single
  source for all ten generated `Tokens.*.axaml` dictionaries. Edit the CSS and rerun
  `assets/themes/generate-tokens.py`; never hand-edit the generated files.

## Verification

Claude drives the app and takes the screenshots (computer-use), rather than handing the
check to the owner. Launch with `&` from the Bash tool so environment variables inherit —
`Start-Process` drops them. Evidence the app writes should go somewhere both sides can see.
Temporary in-process rigs are acceptable for flows a click cannot reach, but must be
removed and residue-checked before every commit.

Lean on the pure layers for confidence: `Core`, the analyzers, and the Avalonia-free
presentation helpers (`ActiveListOps`, `RowFilter`, `DropTarget`, the `*Presenter` types)
are all unit-tested. The thin XAML layer is verified by driving it.
