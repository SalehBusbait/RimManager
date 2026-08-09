`community/communityRules.json` — **hand-written, not a download.** Three invented
packageIds (`some.patchmod`, `some.corelib`) plus one real one, enough to exercise
`loadAfter` / `loadTop` / `loadBottom` parsing. It is deliberately NOT a snapshot of
RimSort's Community-Rules-Database: that database carries no licence at all, so a copy
here would be redistributing material nobody has licensed. Keep it synthetic and small;
`VersionStoryTests` fails if it grows to snapshot size.

The mod fixtures below are real `About.xml` files, chosen to cover the layouts the
scanner has to handle:

- `1718191613` — packageId `VanillaExpanded.VFEMedical` — patches+textures, version subfolders (VFEMedical)
- `2642161586` — packageId `Garethp.ReplaceStuffCompatibility` — asm+defs+patches, version subfolders 1.4/1.5 (ReplaceStuffCompatibility)
- `1508850027` — packageId `Jaxe.RimHUD` — asm+textures, root-only (Jaxe.RimHUD)
- `1542004942` — packageId `phomor.CraftingQualityRebalanced` — asm-only (CraftingQualityRebalanced)
- `1279012058` — packageId `Mehni.PickUpAndHaul` — xml-only, version subfolders 1.0-1.6 (PickUpAndHaul)
- `2009463077` — packageId `brrainz.harmony` — the Harmony framework itself, tiering anchor (brrainz.harmony)
