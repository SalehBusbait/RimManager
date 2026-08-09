# Sorting

Sort rearranges your active mod list so that every ordering rule the app knows
about is honoured. It only ever changes the list on screen — nothing touches the
game until you apply, and applying writes exactly one file (ModsConfig.xml,
backed up first, and refused while RimWorld is running).

You can sort from the toolbar's Sort button, from Tools > Sort load order, or
with Ctrl+Shift+S. The button's dropdown and Tools > Sort with… let you force a
mode: Topological (rules) is the normal rule-driven sort, and Alphabetical
within separators simply alphabetises each of your groups instead.

## Where the rules come from

Three places, in a fixed order of authority:

- Each mod's own metadata. Authors declare "load me after X" or "load me before
  Y" in the mod itself. These rules are facts about the mod and cannot be edited
  — the rule editor shows them with a "locked" pill.
- The community rules database, maintained by the RimSort project. It fills the
  gaps for mods whose authors declared nothing. It syncs automatically at every
  startup, and you can resync any time with Tools > Sync community rules.
- Your own rules, written in the rule editor. Yours beat both of the above.

The status bar keeps a running note on the database — something like
"Community rules 3,412 · synced 2d" with a green tick. If you are offline it
reads "cached" instead: the rules still work, they just have not been confirmed
current. Settings > Integrations has the same story as a pill (active, off, or
sync failed) next to the "Load-order rules" card.

## What a sort produces

Core and any official expansions you own are anchored at the top, in the game's
own order; Sort never places a regular mod above them. Everything else is
arranged to satisfy the rules.

The result is stable: the same mods and the same rules give exactly the same
order every time. Sorting an already-sorted list changes nothing, so you can
press Sort as often as you like without churning your list.

## When rules go round in circles

Occasionally the rules contradict each other — mod A must load after B, B after
C, and C after A. No order can satisfy all three. Instead of giving up, the sort
drops one rule from the loop, finishes, and tells you about it.

You will find it in the Warnings dock under the CYCLES heading, as a row like
"Cycle of 3 broken — dropped edge". Select it and the detail panel titled
"Dependency cycle broken to finish the sort" shows the whole chain of "loads
after" steps, with the dropped one struck through, and a note on why that one
was chosen. The sort prefers to drop a community rule over one declared by a
mod's author, and one of your own rules over either — it breaks the rule you
can change, not one you cannot. Nothing is wrong with your list; the struck
rule is simply not being honoured.

Two buttons sit below the chain:

- Accept dropped edge pins the choice, so every later sort drops the same rule
  instead of re-deciding. The pin belongs to the current modlist — switch to
  another modlist and it does not follow.
- Edit rule opens the rule editor on the mods involved, if you would rather
  switch the conflicting rule off or write your own.

Cycles are one view of the Warnings dock; the conflict marks that often lead you
there are covered in [conflicts](conflicts.md).

## The rule editor

Tools > Rule editor… opens a window you can keep beside the list while you work
— it does not block the app. The left side lists your mods with how many rules
touch each ("4 · 2 off" means two are switched off); the "Only mods with rules"
toggle and the filter box narrow it down. Select a mod and the right side shows
every rule involving it, each tagged with its source: About.xml, community, or
yours.

To add a rule of your own, use the "ADD YOUR OWN RULE" panel at the bottom:
pick "loads after" or "loads before", choose the other mod, and press Add. Your
rules carry a "yours" pill and always win — if the community database says the
opposite, your rule is the one the sort obeys. Remove deletes one of yours
outright.

Community rules cannot be deleted, but each has a Switch off button. A rule you
switch off stays in the list, dimmed and remembered as disabled, so a database
resync can never quietly bring it back. Switch on restores it whenever you
change your mind. If you would rather ignore the database entirely, untick
"Use community rules when sorting and validating" in Settings > Integrations.

After any rule change, press Sort again — rules describe the order, but only a
sort applies them to the list.
