# Phase 17A — Streamer.bot 1.0.5-beta.6 investigation

## Status

Phase 17A is complete. Automated preparation, exact host identification, the
four-profile bridge build, exact CPH catalogue capture, in-application runtime,
deployment lifecycle, and cross-version import/export verification passed.

The existing folders named `Streamer Bot 1.0.5 beta` were inspected and both
contain `1.0.5-beta.1`. Their evidence remains assigned to beta.1. It must not
be relabelled as beta.6 evidence.

Foundry accepts `1.0.5-beta.6` in project manifests and test definitions. Its
CPH completion, signature help, references, and built-in snippets now use the
captured beta.6 interface fingerprint. Beta.1 and beta.6 each expose 564 public
CPH overloads, with zero additions and zero removals between them.

## Disposable host preparation

1. Download or copy the authorised Streamer.bot 1.0.5-beta.6 portable archive.
2. Extract it into a new folder; do not overwrite stable, alpha.34, beta.1, or a
   production Streamer.bot installation.
3. Start it once, decline or omit production accounts, then close it.
4. Confirm the executable version:

   ```powershell
   (Get-Item "PATH_TO_BETA6\Streamer.bot.exe").VersionInfo.ProductVersion
   ```

   The result must be `1.0.5-beta.6`.

## Gate 1 — bridge and dependency compatibility

Run the exact-version compatibility script:

```powershell
.\eng\verify-streamerbot-hosts.ps1 `
  -StablePath "J:\Creators Forge\Streamer Bot 1.0.4 Stable" `
  -AlphaPath "J:\Creators Forge\Streamer Bot 1.0.5 alpha" `
  -BetaPath "J:\Creators Forge\Streamer Bot 1.0.5 beta" `
  -Beta6Path "PATH_TO_BETA6"
```

The script refuses a folder whose executable version does not match its
profile. Retain `artifacts/streamerbot-compatibility/compatibility-report.json`
and the generated beta.6 probe folder.

## Gate 2 — exact CPH catalogue capture

Generate a candidate catalogue to a review file first:

```powershell
dotnet run --project `
  .\tools\CreatorsForge.Foundry.CphCatalog.Generator -- `
  .\artifacts\streamerbot-compatibility\streamerbot-cph-beta6-candidate.json `
  "J:\Creators Forge\Streamer Bot 1.0.4 Stable" `
  "J:\Creators Forge\Streamer Bot 1.0.5 alpha" `
  "J:\Creators Forge\Streamer Bot 1.0.5 beta" `
  "PATH_TO_BETA6"
```

Review the beta.1-to-beta.6 method and overload diff. Only after review should
the candidate replace the embedded catalogue. Then remove the beta.1 fallback
and `CFC0004`, update the revision documentation, rebuild, and rerun all tests.

## Gate 3 — package and runtime verification

Using a beta.6-targeted disposable Foundry sample:

1. Build and confirm the managed DLL, deterministic `CPHInline.cs`,
   stable-v23 import artifact, and package IR are present.
2. Install the DLL through Foundry and import the generated code in beta.6.
3. Add the installed DLL as a compiler reference and compile the C# action.
4. Run the action and confirm the Foundry log message appears.
5. Confirm deployment status is Healthy, then exercise repair, rollback, and
   uninstall in the disposable host.
6. Run Test Explorer and the four-profile mock compatibility matrix; every cell
   must pass.

## Gate 4 — representative import/export

The beta.6 capture contains the generated Hello Foundry action, command, queue,
command trigger, Execute C# bridge, and relative DLL reference. It is retained
under:

```text
experiments/StreamerBotCompatibility/captures/raw/phase17a/
  beta-1.0.5-beta.6
```

Inspection found envelope version 24, minimum version `1.0.0-alpha.1`, exact
`exportedFrom` value `1.0.5-beta.6`, five GUIDs, and one sanitized absolute
compiler-reference path. No credentials or production data were retained in
the normalized fixture. The beta.6 export imported and compiled in 1.0.4
stable, 1.0.5-alpha.34, 1.0.5-beta.1, and 1.0.5-beta.6.

## Acceptance record

| Check | Result |
| --- | --- |
| Exact executable version | Passed — `1.0.5-beta.6` |
| Bridge/dependency probe and hashes | Passed — four exact-version builds |
| Interface fingerprint and catalogue diff | Passed — SHA-256 `d84df720...e67be79`; 0 added, 0 removed |
| Build/package/mock matrix | Passed — beta.6 package built; mock matrix 4/4 passed |
| DLL install, import, compile, execute, log | Passed — product-owner verified in beta.6 |
| Deployment health/lifecycle | Passed — Healthy, modified-file detection, repair, update, rollback, protected recovery, and uninstall verified |
| Representative export and cross-import | Passed — normalized beta.6 capture; import and compilation accepted by all four hosts |
| Stable/alpha.34/beta.1 regression | Passed — four-host bridge build and 200-test regression baseline |

Phase 17A completed on 2026-08-03. The main compatibility matrix includes the
new exact prerelease profile; compatibility is not implied for later builds.
