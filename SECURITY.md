# Security policy

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's
[security advisory form](https://github.com/SalehBusbait/RimManager/security/advisories/new)
rather than a public issue. RimManager is maintained by one person; reports are
handled on a best-effort basis, and you can expect an initial response within a
week.

## Scope

Relevant surfaces, for orientation:

- RimManager writes exactly one file into the game installation
  (`ModsConfig.xml`), after a timestamped backup, never while the game runs.
- Application data is stored locally under `%LocalAppData%\RimManager` as JSON.
  No telemetry is collected and no data leaves the machine.
- Network access is limited to read-only, keyless public endpoints: Steam's
  Workshop metadata API, the GitHub releases API, and the community mod
  databases named in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- The Workshop updater binds the Steamworks library shipped with the user's own
  copy of RimWorld; SteamCMD, when the user requests it, is downloaded from
  Valve.

Only the latest release is supported. This is pre-release software; there is no
backporting.
