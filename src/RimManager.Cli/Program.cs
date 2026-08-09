using System.CommandLine;
using RimManager.Cli.Commands;

// Phase 1 CLI: the read path. `list` auto-detects the install, scans mods, and
// prints the active load order (or all installed mods with --all).

var root = new RootCommand("RimManager — RimWorld mod manager (CLI)");
root.Subcommands.Add(ListCommand.Build());
root.Subcommands.Add(SortCommand.Build());
root.Subcommands.Add(ExplainCommand.Build());
root.Subcommands.Add(ValidateCommand.Build());
root.Subcommands.Add(ApplyCommand.Build());
root.Subcommands.Add(ModlistCommand.Build());
root.Subcommands.Add(ShareCommands.Export());
root.Subcommands.Add(ShareCommands.Import());
root.Subcommands.Add(ConflictsCommand.Build());
root.Subcommands.Add(CrashLogCommand.Build());
root.Subcommands.Add(WorkshopCommand.Build());
root.Subcommands.Add(GithubCommand.Build());
root.Subcommands.Add(RulesCommand.Build());
root.Subcommands.Add(ModDatabasesCommand.BuildReplacements());
root.Subcommands.Add(ModDatabasesCommand.BuildKnownGood());

return root.Parse(args).Invoke();
