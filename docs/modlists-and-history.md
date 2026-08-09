# Modlists and history

A modlist is a saved load order: which mods are active, in what order, together with
your separators and groups. You can keep as many as you like — one per colony, one
per playthrough, one experimental. Switching between them changes what RimWorld will
load; it never moves, downloads or deletes a mod, and nothing reaches the game until
you press Apply to game. RimManager writes exactly one game file, ModsConfig.xml,
backs it up first, and refuses to write while RimWorld is running.

## Switching lists

The current list's name sits at the left of the toolbar, next to its colour dot.
Click it to open the switcher: a searchable list showing each modlist's colour, name,
how many mods it has active, and a small glyph if that list has unapplied edits or
was changed outside RimManager. The list you are on is ticked, and the default list
wears a small "default" pill.

The footer of the switcher has two buttons: "New modlist…" creates an empty list
where every installed mod starts inactive, and "Manage…" takes you to the Modlists
page in Settings.

## Managing lists in Settings

The Modlists page in Settings shows every list with its mod count, how many settings
files and snapshots it carries, and when it was last used. Select one to work on it:

- "Rename" changes the name.
- "Colour dot" picks the swatch shown in the toolbar and the switcher. Colours are
  palette choices, so they adapt when you change theme.
- "Duplicate" makes a copy — same mods, same order, same separators — that you can
  change without affecting the original. The copy is named after the source, like
  "Main copy".
- "Make default" marks the list that opens when nothing else has been used.
- "Delete…" always asks first, and the confirmation says exactly what goes: the
  list, its snapshots and its saved mod-settings files. Your mods, your saves and
  the game folder are untouched.

Two lists refuse deletion, and the page says why: the default list (make another
list the default first) and your only list (RimManager needs one to load).

Each list also has a "Keep this list's own mod settings" switch, off by default.
Off means the list shares whatever mod settings the game currently has. On gives it
its own copy, captured when you switch away from it — useful when two playthroughs
want the same mod configured differently.

## The History tab

The History tab in the bottom dock records snapshots of your load order. One is
taken every time you apply, and — with "Snapshot before sorting" ticked in the Sort
menu — every time you [sort](sorting.md). Each row shows when it was taken, what
the step was, and a change summary like "+3 −1 · 4 moved" against the state before
it. The chips at the top filter the table: "All", "Applied only" and "Named".

Select a row and the middle pane shows the diff for that step: which mods moved and
how far, which were added, which were removed. Long diffs collapse after eight
lines; "show all" expands them.

## Naming and restoring states

The right-hand pane holds the actions for the selected state. "Restore this state"
brings its arrangement back — but it appends a new state with those contents rather
than rewinding, so the steps in between are still there. Nothing in history is ever
destroyed except by pruning.

Pruning is the "Prune older than 30d" button, which clears out old unnamed
snapshots to save space. To protect a state, name it: press the pencil next to
"Name", type a name, and confirm. A named state wears a star, appears under the
"Named" chip, and is never pruned. Press the small cross next to the name to
un-name it again.

The pane also offers "Export current list as .rwlist" — note that this exports the
list as it is now, not the selected snapshot. To export an old state, restore it
first. See [sharing](sharing.md) for what an .rwlist carries.

## When the game changes the order

The status bar always says how your list relates to the game: "Applied 14:09" or
"In sync" when they match, "Edited — not applied" when you have changes waiting,
"Never applied" for a list that has not been written yet.

Sometimes ModsConfig.xml changes behind RimManager's back — most often when you
click "Load mod list from save" inside RimWorld itself. The status bar then shows
"Changed outside · Review", and a notice appears: "RimWorld's mod list changed
outside RimManager". It offers three ways forward:

- "Review differences…" opens a side-by-side view of your order against the
  game's, with two buttons: "Keep mine" leaves everything as it is, and "Take
  theirs" replaces this list's arrangement with the game's.
- "Save as new modlist" keeps your current list untouched and saves the game's
  order as a new list, named with a timestamp like "RimWorld · 7 Aug 14:09".
- The cross dismisses the notice and keeps your list as it is; the status bar
  entry remains the way back into the review.

An order adopted from the game arrives flat — the game's file carries no
separators — and whichever way you choose, a restorable snapshot lands in History
first, so nothing is lost.

One special case: RimWorld can reset its own mod list to bare Core after a crash.
RimManager recognises this and says so — "RimWorld reset its mod list" — and since
that state is an accident rather than a decision, it simply offers "Apply to game"
to put your list back.
