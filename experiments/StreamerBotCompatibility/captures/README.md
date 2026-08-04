# Representative Streamer.bot export captures

The Phase 1 capture uses the same
`Creators Forge Foundry Compatibility Probe` action in each disposable
Streamer.bot instance.

## Capture metadata

- Name: `Creators Forge Foundry Phase 1 Probe`
- Author: `Creators Forge Foundry`
- Version: `0.1.0`
- Description: `Representative Phase 1 export for Streamer.bot PROFILE`

Replace `PROFILE` with the exact product version.

## Raw files

Save exports from the Streamer.bot **Export to File** action under `raw`:

```text
raw/
├── stable-1.0.4
├── alpha-1.0.5-alpha.34
└── beta-1.0.5-beta.1
```

Preserve the extension selected by Streamer.bot. Raw captures are ignored by
Git until inspection confirms that they contain no credentials, tokens, or
unintended machine-specific data.

The representative files were captured on 2026-07-24. Their safety review found
no credential-like data. They remain ignored because they contain absolute
local compiler-reference paths and opaque bytecode.

## Inspection

For each capture:

```powershell
dotnet run `
  --project ..\CreatorsForge.Foundry.StreamerBot.ExportInspector `
  -- `
  inspect RAW_EXPORT_FILE OUTPUT_DIRECTORY
```

The inspector validates the `SBAE` plus GZip envelope and writes:

- `decoded.json` containing the readable payload;
- `normalized.json` with GUID, timestamp, bytecode, and absolute-path noise
  replaced deterministically;
- `inspection.json` containing hashes and safety findings.

Sanitized copies of `normalized.json` and `inspection.json` are retained under
`normalized/<profile>` as versioned fixtures.
