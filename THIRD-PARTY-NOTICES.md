# Third-party notices

Creators Forge Foundry uses third-party software. This inventory is provided
for release review and is not a substitute for the licence text supplied by
each dependency.

## Direct runtime/build dependencies

| Component | Version used by the source tree | Purpose | Licence identified by upstream package |
|---|---:|---|---|
| AvalonEdit (`ICSharpCode.AvalonEdit`) | 6.3.1.120 | Desktop source editor | MIT |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.6.0 | C# parsing, diagnostics, completion, formatting, and workspace services | MIT |
| Microsoft.NETFramework.ReferenceAssemblies.net481 | 1.0.3 | Offline .NET Framework 4.8.1 reference assemblies for Streamer.bot builds | Microsoft .NET library/reference-assembly terms; review the package licence |
| Microsoft Edge WebView2 SDK | 1.0.4078.44 | Crash-isolated HTML/CSS/JavaScript executable preview | Microsoft software licence terms; the Evergreen WebView2 Runtime is installed separately |
| .NET Desktop Runtime and framework files | 10.x self-contained publish | Foundry application runtime | Microsoft .NET library/runtime terms; review the files in the final publish |

The Roslyn package has transitive Microsoft.CodeAnalysis and supporting
dependencies. The self-contained Windows publish also contains framework and
runtime components not individually enumerated in this short source-level
summary. Before v1 distribution, generate or inspect the resolved dependency
inventory and final publish directory, preserve all required upstream licence
and notice files, and update this document if the shipped set differs.

Test-only packages such as xUnit, Microsoft.NET.Test.Sdk, and coverlet are used
by repository validation and are not intended to be copied into the desktop
distribution. If they appear in the final distribution, stop release and
review the packaging output.

The pinned OBS Studio SDK/source cache is obtained separately for development
and is not redistributed in the Foundry desktop package. Streamer.bot and OBS
Studio are separate third-party applications and are not bundled with Foundry.
Their names identify compatible target hosts and do not imply endorsement.

## Product licence

Creators Forge Foundry is distributed under the proprietary End-User Licence
Agreement in the repository root `LICENSE.md`. Sample projects may contain
separate editable licences generated for those projects; those do not replace
or modify the Foundry product licence.

Release approval requires checking this inventory against the exact shipped
bytes and obtaining appropriate legal review for the intended distribution.
