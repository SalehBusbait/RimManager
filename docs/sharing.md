# Sharing a load order

A load order you have spent an evening tuning is worth handing to a friend in
one piece. RimManager gives you three ways to send one and one wizard for
taking one in. None of them touch your game: importing only changes the list
inside RimManager, and nothing reaches RimWorld until you apply — the app
writes exactly one game file, ModsConfig.xml, backs it up first, and refuses
while the game is running.

## Export as a .rwlist file

File > Export mod list… saves your active arrangement as a single small file.
It carries the order, your separators and their colours, tags, notes, and the
version of each mod — everything needed to reproduce the list, but not the
mods themselves. Send it over Discord, email, anywhere a file goes. The format
is plain JSON and documented in [rwlist-v1](rwlist-v1.md) if you are curious
what is inside.

## Export as a Workshop item

File > Export as Workshop item… writes a folder shaped like a RimWorld mod
with your list inside it, then opens that folder. Uploading is your act:
RimWorld's own dev-mode uploader takes the folder from there, the same way any
mod is published. Anyone who subscribes to the result sees it appear in
RimManager as an import offer (described below) rather than as a mod that does
anything in game.

## Export as a Steam collection

File > Export as Steam collection… creates a private collection on your own
Steam account, named after your modlist and containing the Workshop mods in
your load order. It needs the Steam client running and logged in. A collection
can only hold Workshop items, so local mods are left out and the confirmation
tells you how many. The dialog asks "Create a Steam collection?" and the
button reads Create collection.

The collection starts private and stays private: RimManager opens its page so
you can look it over, and publishing it — or deleting it — happens there, by
you, on Steam's own page. Steam may briefly show you as in-game while the
collection is created.

## Importing a .rwlist or ModsConfig.xml

File > Import mod list (.rwlist, .xml)… reads a shared file and loads its
arrangement into your current list — order and separators included. The status
bar then reports how many of its mods you have installed, how many are
missing, and how many you have at a different version than the sender.
Missing mods are reported, not fetched; if the sender also made a collection,
importing that is the way to download what you lack.

If the file was edited or damaged after export, a checksum warning says so.
You can also import your own ModsConfig.xml this way, which is a quick way to
pull in an order you arranged in the game itself.

## When a Workshop item is a mod list

If you subscribe to a list someone published as a Workshop item, RimManager
notices and offers it once on a strip above your mods: "Workshop item
'So-and-so' looks like a mod list", with an Import… button. Importing opens a
small dialog and, if you confirm with Import as modlist…, creates a new
modlist from the payload — your current list is never touched, and the status
bar tells you how many of its mods you have. Choose Not now and the item
simply stays in your inactive pane; you can come back to it any time by
right-clicking the row and choosing Import mod list….

## Importing a Steam collection

File > Import Steam collection… opens a two-step wizard. Paste a collection
URL or its ID into the Collection URL or ID box and press Fetch. Fetching only
reads: nothing is subscribed, downloaded or changed yet.

Once the collection resolves, a summary titled "What RimManager found" counts
its members four ways: already installed, need downloading, unavailable (items
removed from the Workshop), and already active in your list. Below it, "How to
add them" is the choice that matters:

- Append as a new separator group — the default. Your current order is
  untouched; new mods land in a group named after the collection, so an
  import you regret is easy to see and remove.
- Merge and sort everything — adds the mods and runs a full
  [sort](sorting.md) afterwards. A snapshot is taken first.
- Replace my load order — the only destructive choice, and it says exactly
  what it will do: how many of your mods would be deactivated. Core and DLC
  always stay, and a snapshot makes it reversible.
- Create a new modlist — your current list stays as it is, and the members
  you tick form a new modlist in collection order.

The button then reads Review, with the item count, and nothing downloads
until the next step.

## The review step

Step 2 lists every member with its state and download size. Tick or untick
rows to choose exactly what joins your load order; unavailable items cannot be
ticked at all. A View collection button opens the collection's page if you
want to read descriptions first.

If some mods need downloading, "How to get the missing ones" offers two
routes:

- Subscribe in Steam — opens the collection in Steam, where Subscribe to all
  fetches everything and keeps it updated by Steam afterwards. Steam takes
  the whole collection; your ticks still decide what joins the load order.
- Download via SteamCMD — needs no account and works with Steam closed. Mods
  land in your Mods folder as ordinary local copies, so RimManager is the
  only thing tracking their updates. This route downloads only what you
  ticked.

The final button never says just "Import" — it names what it is about to do,
such as how many it will download and how many it will add. Downloads run in
the background, so you can keep working while they arrive; downloaded mods
come in inactive, placed according to the strategy you chose in step 1.
