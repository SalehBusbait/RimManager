# Contributing

Thank you for considering a contribution. RimManager is maintained by one person;
clear, well-scoped issues and pull requests are the most useful thing you can offer.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download), pinned in
`global.json`. The solution file is `RimManager.slnx` — the XML solution format.

```
dotnet build RimManager.slnx -c Release
dotnet test  RimManager.slnx -c Release
dotnet run --project src/RimManager.App
```

Warnings are treated as errors in Release configuration. The full test suite must
pass; tests that need the network or a RimWorld install skip cleanly when those are
absent, so a bare clone builds and tests green.

## Conventions

The architecture and the project's non-negotiable conventions are documented in
[CLAUDE.md](CLAUDE.md). The short version:

- `RimManager.Core` performs no I/O and has no UI. Anything testable lives below
  the Avalonia shell, not in it.
- JSON is the source of truth for user data; SQLite is a disposable cache.
- No literal colours outside the theme dictionaries; tag and separator colours
  persist as palette indices.
- Behavioural changes come with tests. Where a defect was invisible to the
  compiler (an optional parameter dropped, a control wired to nothing), add a
  guard test so it cannot return silently.

Commit messages are short, imperative and professional, with no trailers.

## Pull requests

Keep them scoped to one change. State what the change does and how it was
verified. UI changes benefit from a before/after screenshot — Avalonia fails
silently, and a picture catches what a diff cannot.

By contributing you agree that your contribution is licensed under the project's
[MIT Licence](LICENSE).
