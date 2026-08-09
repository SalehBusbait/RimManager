# Conflicts

Two mods often touch the same piece of the game — the same animal, weapon, recipe or
texture. RimWorld does not merge them: for most content, the version that loads last is
the one the game actually uses, and every earlier version is silently discarded. RimManager
scans your mods, finds these overlaps, and marks them on the rows so you can see who wins
before you ever launch the game.

Conflicts are not errors. A large modlist always has them, and most are intentional —
a retexture is *supposed* to overwrite the original art. The marks exist so that when
something looks wrong in game, you can find out which mod had the last word.

## The marks on a row

An active mod involved in a conflict wears a small bolt at the end of its row. The bolt's
colour tells you what kind of involvement it is:

- A yellow bolt means override conflicts only — contested content where one version wins
  and the others are discarded.
- A blue bolt means Harmony is involved: this mod shares Harmony patch targets with
  another mod (possibly alongside overrides — the mark next to it says which).
- No bolt means no conflict at all.

Beside the bolt sits a smaller mark that carries the override story:

- A green plus: this mod's contested content wins everywhere it competes.
- A red minus: this mod is overwritten — another mod's version is used instead.
- Both stacked together: it wins some contests and loses others.

Hover the badge for the numbers, for example "Contested content: wins 2 · overwritten
in 1 — last loaded wins". Selecting a row also tints the other rows it contends with, so
you can see its relationships directly in the list.

## The Mod conflicts window

Double-click an active row, or click its bolt badge, to open the Mod conflicts window
for that mod. It lists every live contest in three fixed sections:

- OVERWRITTEN — contests this mod loses. Their version loads after this mod's; last
  loaded wins, and theirs is what the game uses. Each row names the winner and its
  position in your load order.
- OVERWRITES — contests this mod wins. It loads last, so its version is what the game
  uses. Each row names who it beats.
- HARMONY — shared patch targets. Every patch runs; order decides the outcome, and
  there is no winner to pick.

Each row shows the kind of contest (Def override, XML patch, Texture or Harmony) and the
exact thing being contested. Where both versions could be read, a Diff button opens a
side-by-side comparison of the two versions of the contested element, winner on the right.

Overlaps where every mod ships an identical copy are hidden — they change nothing —
and the window's footer tells you how many, for example "12 identical overlaps hidden".
The window is a snapshot from the moment it opened: keep it up while you reorder in the
lists behind it, then reopen it for a fresh reading.

## What a Def override is

Almost everything in RimWorld is described by a definition — a "def" — for each item,
animal, recipe, storyteller and so on. When two mods each ship their own full version of
the same def, the game keeps only the last one loaded. That is a Def override: not a
crash, not a merge, just a complete replacement. An XML patch conflict is the finer
version of the same thing — two mods editing the same part of a def rather than
replacing it whole. A Texture conflict is two mods shipping art for the same image;
there is no text to compare, so those rows have no Diff button.

## Why Harmony rows name no winner

Many mods use Harmony to change how the game behaves rather than what it contains. When
two mods patch the same target, nothing is discarded — both patches run, one after the
other, and the combined result depends on the order. That is why the HARMONY section
never says who wins and never offers Win this: there is no loser to rescue. If two
Harmony mods misbehave together, the fix is reordering or removing one, not picking a
winner. The window's legend keeps the distinction on screen: overwritten — theirs is
used; overwrites — this mod's is used; Harmony — linked, not ranked.

## Win this

Rows in the OVERWRITTEN section carry a Win this button. It moves the mod below the
current winner so it loads last and takes effect — one undoable move in your list, the
same as dragging it there yourself. Nothing touches your game files until you apply the
list, and the change is yours to undo like any other move.

Use it deliberately. The mod that loads last was often put there for a reason, and
[sorting](sorting.md) may move things again on the next sort. Win this is for the case
where you have looked at the diff, decided you prefer the losing version, and want the
order to say so.

## Inactive mods conflict with nothing

A mod that is not in your active list overrides nothing and patches nothing, so it wears
no badge and appears in no one's conflict window. Deactivating a mod is always a clean
way out of a contest. The other side of that coin: a mod you activated since the last
scan is not yet in the conflict picture, so give the scan a moment to catch up — if you
open a window while it is still running, the window says so and asks you to reopen it
when it finishes.
