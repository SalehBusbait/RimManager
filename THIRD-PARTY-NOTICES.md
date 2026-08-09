# Third-Party Notices

RimManager incorporates components from the projects listed below. The licences under
which these components are made available are set out here, and are unaffected by the
terms under which RimManager itself is distributed. These notices must accompany any
redistribution of a RimManager build.

Component versions are held centrally in `Directory.Packages.props`. This file records
which components are used and under which terms.

## Components distributed with the application

| Component | Licence | Project |
|---|---|---|
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Desktop | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Themes.Fluent | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter (typeface) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.DependencyInjection | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw.lib.e_sqlite3 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | Public domain | https://www.sqlite.org |
| Mono.Cecil | MIT | https://github.com/jbevain/cecil |
| System.CommandLine | MIT | https://github.com/dotnet/command-line-api |

## Components used for development and testing only

These are not distributed with the application.

| Component | Licence | Project |
|---|---|---|
| xUnit.net | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Xunit.SkippableFact | MIT | https://github.com/AArnott/Xunit.SkippableFact |
| Microsoft.NET.Test.Sdk | MIT | https://github.com/microsoft/vstest |
| FluentAssertions 7.x | Apache-2.0 | https://github.com/fluentassertions/fluentassertions |

FluentAssertions is constrained to the 7.x line. Releases from 8.0 onward are published
under a commercial licence.

## Components not distributed with the application

**Steamworks API.** The Workshop updater binds exported functions of `steam_api64.dll`
as supplied with the user's own installation of RimWorld. No part of the Steamworks SDK
is distributed with RimManager, and no Steamworks headers or binaries are contained in
this repository. Use is governed by the Steamworks SDK Access Agreement between Valve
Corporation and the publisher of that copy.

**SteamCMD.** Valve's content-delivery client is retrieved from Valve on demand, at the
user's request, into a directory managed by RimManager. It is not redistributed here and
is governed by Valve's own terms.

## Data retrieved at runtime

The following datasets are downloaded to the user's machine during operation and cached
locally. RimManager does not modify, redistribute, or assert any ownership of them, and
no copy of any of them is contained in this repository or in any distributed build.

| Dataset | Maintainer | Licence |
|---|---|---|
| [Community Rules Database](https://github.com/RimSort/Community-Rules-Database) | The RimSort project | None declared |
| [UseThisInstead](https://github.com/emipa606/UseThisInstead) | Mlie | MIT |
| [NoVersionWarning](https://github.com/emipa606/NoVersionWarning) | Mlie | MIT |

The Community Rules Database carries no licence declaration. Retrieval and local use are
the purposes for which it is published; redistribution is not permitted in the absence of
a grant, and RimManager therefore does not bundle it in any form.

## Attribution

RimManager was inspired by [RimSort](https://github.com/RimSort/RimSort).

RimManager is not affiliated with, endorsed by, or connected to Ludeon Studios. RimWorld
is a trademark of Ludeon Studios.
