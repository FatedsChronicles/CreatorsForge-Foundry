# Phase 25H — Native operation capture procedure

Foundry expands its native Streamer.bot catalogue from retained evidence, not
operation names or remembered numeric type values. The Export Inspector reads
the SBAE/GZip/JSON envelope without executing actions, scripts, C#, or bytecode.

## Candidate capture set

Capture these initial sub-actions independently:

1. Delay
2. Log
3. Set Global Variable
4. Get Global Variable
5. Send Message
6. Execute Action
7. Set Command State
8. Set Command Group State

For every candidate and host, create two disposable actions:

- **Defaults** — add the sub-action and change only fields required to save it.
- **Populated** — use unmistakable, non-secret fixture values in every visible
  field and toggle each Boolean away from its default where possible.

Do not include account credentials, tokens, passwords, private URLs, real user
names, or machine-specific paths. Do not run either action.

## Supported capture hosts

- Streamer.bot 1.0.4
- Streamer.bot 1.0.5-alpha.34
- Streamer.bot 1.0.5-beta.1
- Streamer.bot 1.0.5-beta.6
- Streamer.bot 1.0.7

Export one candidate action at a time. Save the raw export outside Git and use:

```powershell
dotnet run --project experiments/StreamerBotCompatibility/CreatorsForge.Foundry.StreamerBot.ExportInspector -- inspect <export-file> <output-directory>
```

The output contains decoded JSON for local review, normalized JSON, and an
inspection report. `nativeOperations` contains only:

- `entityKind`
- `nativeType`
- `occurrences`
- sorted `property:type` signatures

It does not contain property values. Raw and decoded files remain untracked
until reviewed for credentials and machine-specific data. Only sanitized,
normalized fixtures and value-free reports may be committed.

## Approval checklist

An operation may enter the built-in catalogue only when:

- its type and property contract are understood from both default and populated
  captures;
- differences across all five hosts are classified;
- fields have explicit types, defaults, constraints, and compatibility;
- v23/v24 import and export are deterministic;
- unknown fields and opaque siblings survive matching-format round trips;
- the generated package imports into every declared host;
- no imported code is executed during inspection or testing.

Conversion to C# additionally requires an official CPH method contract and
behavioral equivalence tests. A verified native mapping does not automatically
qualify for conversion.
