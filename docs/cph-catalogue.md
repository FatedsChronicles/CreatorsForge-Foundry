# CPH catalogue and editor intelligence

Phase 4B adds a versioned local CPH catalogue shared by completion, signature
help, documentation, compatibility diagnostics, and build metadata.

## Catalogue v1

Revision `1.0.0+0b24468390c6` was generated from the physical
`Streamer.bot.Plugin.Interface.dll` files supplied for:

- `1.0.4-stable`;
- `1.0.5-alpha.34`;
- `1.0.5-beta.1`;
- `1.0.5-beta.6`.

It contains 512 unique method names and 564 overloads. Five hundred methods are
available to the stable profile. Twelve additional methods are present in all
three 1.0.5 prerelease profiles. Beta.1 and beta.6 expose the same 564 public
overloads; their interface assembly fingerprints differ.

Every overload records its signature, return type, parameters, optional
defaults, and exact profile availability. Every method records category,
platform, status, minimum version, related methods, cautions, and documentation
fields. Core methods such as `TryGetArg`, `SetArgument`, `SendMessage`,
`RunAction`, `SetGlobalVar`, logging, `GetVersion`, and `Wait` have curated
summaries, parameter guidance, examples, and official-reference routes.
Remaining inventory entries retain verified signatures and conservative
generated descriptions rather than invented behavioral claims.

## Editor behavior

Type `CPH.` to open completion. Results are filtered to the project's
`target.profile`. `Ctrl+Space` reopens completion while the caret remains after
`CPH.`.

Completion details show overload signatures, category, summary, exact profile
availability, deprecation status, and curated examples and reference routes.
Typing `(` opens overload and active-parameter help. A comma refreshes the
active parameter. The searchable local reference is available under
**Code > CPH Method Reference**.

## Compatibility diagnostics

| Code | Severity | Meaning |
| --- | --- | --- |
| `CFC0001` | Error | Method or matching overload unavailable for the selected profile |
| `CFC0002` | Warning | Deprecated method used |
| `CFC0003` | Error | Method does not exist in catalogue v1 |

Diagnostics contain source locations and suggested fixes and appear beside
Roslyn diagnostics in Problems.

## Generation

The generator performs read-only inspection through `MetadataLoadContext`; it
does not execute Streamer.bot code:

```powershell
dotnet run --project `
  .\tools\CreatorsForge.Foundry.CphCatalog.Generator -- `
  .\src\CreatorsForge.Foundry.Editor\Catalogs\streamerbot-cph-v1.json `
  "PATH_TO_1.0.4_STABLE" `
  "PATH_TO_1.0.5_ALPHA" `
  "PATH_TO_1.0.5_BETA_1" `
  "PATH_TO_1.0.5_BETA_6"
```

Output ordering, formatting, interface fingerprints, and revision hashing are
deterministic. Regeneration must be reviewed before replacing the embedded
catalogue. Prerelease interface data from this private development workspace
must not be redistributed without the applicable permission.

The beta.6 directory is optional only when reproducing the historical
three-profile catalogue. Release catalogue generation includes it and records
its exact interface fingerprint and overload availability.

## Build traceability

`build/package-ir.json` records `target.cphCatalogueRevision`, binding a build
to the compatibility facts used by the editor.
