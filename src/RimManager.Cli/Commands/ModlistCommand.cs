using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>modlist</c> command group — the CLI twin of the GUI's modlist switcher.
/// <para>
/// Replaces <c>instance</c> and <c>profile</c> (see the modlist migration). Those
/// still read the old <c>instances/</c> tree, which the GUI has migrated away from, so
/// after a single GUI launch the two would be editing different files and neither would
/// see the other's work. They warn about that rather than being left to look fine.
/// </para>
/// </summary>
internal static class ModlistCommand
{
    public static Command Build()
    {
        var command = new Command("modlist", "List, inspect and switch mod lists.");
        command.Subcommands.Add(BuildList());
        command.Subcommands.Add(BuildShow());
        command.Subcommands.Add(BuildUse());
        return command;
    }

    private static ModlistRepository Repo() => new(new PhysicalFileSystem());

    /// <summary>
    /// Resolves by id, then by exact name. Never auto-creates: on the CLI, seeding a list
    /// from whatever <c>ModsConfig.xml</c> happens to say is a side effect nobody asked
    /// for, and the GUI is where first-run belongs.
    /// </summary>
    private static Modlist? Find(ModlistRepository repo, string idOrName)
    {
        var all = repo.List();
        return all.FirstOrDefault(l => l.Id == idOrName)
            ?? all.FirstOrDefault(l =>
                string.Equals(l.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    private static int NoLists()
    {
        Console.Error.WriteLine(
            "No modlists yet. Start RimManager once — first run converts any existing "
            + "instances and seeds a list from the game's current load order.");
        return 1;
    }

    private static Command BuildList()
    {
        var cmd = new Command("list", "List every modlist.");
        cmd.SetAction(_ =>
        {
            var all = Repo().List();
            if (all.Count == 0) return NoLists();

            foreach (var l in all)
            {
                var mods = l.State.Entries.Count(e => e.Kind == ModlistEntryKind.Mod);
                var separators = l.State.Entries.Count(e => e.Kind == ModlistEntryKind.Separator);
                var marks = string.Concat(
                    l.IsDefault ? "*" : " ",
                    l.CapturesModSettings ? "s" : " ",
                    l.Locked ? "L" : " ");

                Console.WriteLine(
                    $"  {marks} {l.Id}  {l.Name,-28} {mods,5} mods  {separators,3} sep  "
                    + $"{l.GameVersion ?? "?"}");
            }

            Console.WriteLine();
            Console.WriteLine("  * default   s captures mod settings   L locked");
            return 0;
        });
        return cmd;
    }

    private static Command BuildShow()
    {
        var nameArg = new Argument<string>("name") { Description = "Modlist id or name." };
        var cmd = new Command("show", "Print a modlist's load order.") { nameArg };

        cmd.SetAction(result =>
        {
            var repo = Repo();
            if (repo.List().Count == 0) return NoLists();

            var wanted = result.GetValue(nameArg)!;
            if (Find(repo, wanted) is not { } list)
            {
                Console.Error.WriteLine($"No modlist '{wanted}'. Run `modlist list`.");
                return 1;
            }

            Console.WriteLine($"{list.Name}  ({list.Id})");
            if (list.LastAppliedUtc is { } applied)
                Console.WriteLine($"  last applied {applied:u}");
            Console.WriteLine();

            var index = 0;
            foreach (var entry in list.State.Entries)
            {
                if (entry.Kind == ModlistEntryKind.Separator)
                {
                    Console.WriteLine($"      -- {entry.DisplayName} --");
                    continue;
                }

                index++;
                var off = entry.Enabled ? " " : "x";
                Console.WriteLine($"  {off} {index,4}  {entry.Id,-42} {entry.DisplayName}");
            }

            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// Marks a list as the most recently used, which is what the GUI opens next.
    /// <para>
    /// It deliberately does NOT write <c>ModsConfig.xml</c>. Applying is a separate,
    /// explicit act in this app — the toolbar's Apply raises a commit bar rather than
    /// writing — and a switch that silently changed what the game loads would be the one
    /// surprise the whole modlist model exists to remove. Run <c>apply</c> for that.
    /// </para>
    /// </summary>
    private static Command BuildUse()
    {
        var nameArg = new Argument<string>("name") { Description = "Modlist id or name." };
        var cmd = new Command("use", "Select a modlist (does not write ModsConfig.xml).") { nameArg };

        cmd.SetAction(async result =>
        {
            var repo = Repo();
            if (repo.List().Count == 0) return NoLists();

            var wanted = result.GetValue(nameArg)!;
            if (Find(repo, wanted) is not { } list)
            {
                Console.Error.WriteLine($"No modlist '{wanted}'. Run `modlist list`.");
                return 1;
            }

            await repo.MarkUsedAsync(list);
            Console.WriteLine($"Selected '{list.Name}'. Run `apply` to write it to the game.");
            return 0;
        });
        return cmd;
    }
}
