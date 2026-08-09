# `.rwlist` v1 — shareable RimWorld modlist format

A `.rwlist` is a single JSON file that carries a full modlist arrangement —
order, separators, tags, categories, per-mod notes — so a list can be shared and
reproduced, not just its packageIds. This document is the authoritative schema.

This design deliberately resolves three problems in the original spec draft:

1. **One grouping model, not two.** The draft made mods carry a `separatorId`
   *and* placed separators inline in an ordered array — two sources of truth that
   can disagree. Here grouping is **purely positional**: `entries` is ordered, and
   a mod belongs to the nearest separator above it. There is no `separatorId`.
2. **Honest fidelity.** The draft claimed "carry everything" but dropped tag
   colors, favorite, alias, color-override, and ignore-updates. Here tag/category
   *definitions* (with colors) live at the top level and mods reference them by id,
   and every per-mod metadata field is represented.
3. **A defined checksum.** See [Checksum](#checksum).

## Top-level object

```jsonc
{
  "schemaVersion": 1,
  "name": "Kitchen Sink 1.6",
  "author": "…",
  "description": "…",
  "createdUtc": "2026-07-25T12:00:00Z",
  "gameVersion": "1.6",
  "requiredDlc": ["ludeon.rimworld.royalty", "ludeon.rimworld.odyssey"],
  "tags":       [ { "id": "t1", "name": "framework", "paletteIndex": 4, "color": "#C77DDF" } ],
  "categories": [ { "id": "c1", "name": "Core", "parentId": null } ],
  "entries":    [ /* ordered mods + separators, see below */ ],
  "userRules":  [ { "before": "a.b", "after": "c.d" } ],
  "checksum": "sha256:…"
}
```

- `requiredDlc` — DLC packageIds the list needs (informational; a reconcile step
  checks them against what's owned).
- `tags` / `categories` — definitions, so colors and the category tree survive
  export. Entries reference them by `id`.
- **Colour is a `paletteIndex` (0–5: blue, green, amber, red, violet, slate), not a
  hex.** RimManager persists an index rather than a colour so a user's tags and
  separators flip correctly between the light and dark themes — a stored `#4FBF87`
  is the *dark* green and is illegible on a light background. `color` is written
  alongside as an **advisory** value, because a `.rwlist` is read by other people's
  tools and a bare index means nothing to them. On import `paletteIndex` wins; a
  file carrying only `color` is mapped to the nearest of the six hues. Both fields
  are optional, so a v1 file still loads.
- `userRules` — the user-source load-order edges needed to reproduce this exact
  order among the list's mods (`before` loads before `after`).

## Entries

`entries` is an **ordered** array. Each element is either a mod or a separator,
distinguished by `type`.

```jsonc
{ "type": "separator", "id": "sep-1", "name": "Frameworks", "paletteIndex": 4, "color": "#C77DDF", "collapsed": false }
```

```jsonc
{
  "type": "mod",
  "packageId": "brrainz.harmony",     // canonical identity (lowercased)
  "displayName": "Harmony",
  "source": "workshop",                // workshop | local | git | dlc | pinned
  "publishedFileId": "2009463077",     // workshop id, if any
  "gitUrl": null,
  "gitRef": null,                       // commit/tag for reproducible git mods
  "modVersion": "2.4.2.0",             // INFORMATIONAL — see reproducibility note
  "pinned": false,
  "tagIds": ["t1"],
  "categoryId": "c1",
  "note": "must be first",
  "alias": null,                        // custom display name
  "colorOverride": null,
  "favorite": false,
  "ignoreUpdates": false
}
```

A mod's group membership is **positional** (the separator above it) — there is no
back-reference.

### Reproducibility note

`modVersion` and `publishedFileId` are enough to *identify* a mod but not to
*reproduce a specific version*: the Steam Workshop only serves the latest build.
Exact-version reproduction requires either `pinned: true` with a bundled copy
(`.rwlist.zip`, a later addition) or, for git mods, `gitRef`. Importers treat
`modVersion` as advisory and surface a version-mismatch in reconciliation.

## Checksum

Optional. Format `sha256:<hex>`. Computed over the UTF-8 JSON serialization of the
manifest **with the `checksum` property removed** (never over the file including
its own checksum). It detects corruption/tampering, not authenticity. Importers
verify it when present and warn on mismatch; they never refuse to load on a bad
checksum (the user may want the data anyway).

## Import reconciliation

Importing is a workflow, not a dialog. For each mod the importer reports one of:
`Installed` (present, ready), `Missing` (not installed — needs downloading),
`VersionMismatch` (installed but a different `modVersion`), or `Unavailable`
(delisted/no source). The user resolves these before applying.

## Other export targets

`.rwlist` is full fidelity. Lossy convenience targets, derived from the same
arrangement:

- **`ModsConfig.xml`** — drop-in active list (order + packageIds only).
- **Markdown / BBCode** — forum/Discord posts; separators become headings, mods
  become links to their Workshop pages.
- **CSV** — spreadsheet export (index, packageId, name, source, version, tags).

Import currently accepts `.rwlist` and `ModsConfig.xml`. RimPy/RimSort exports are
largely `activeMods` lists and import through the same path; `.rws` save import and
Workshop-collection-URL import are later additions.
