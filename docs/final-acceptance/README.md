# Foundry v1 final acceptance

Phase 16 is the release-candidate gate for Creators Forge Foundry v1. It proves
the complete creator journey on a clean Windows x64 machine and records the
evidence needed for a deliberate release decision.

Passing the automated suite is necessary, but it is not the same as passing the
release gate. Streamer.bot imports, compiler references, action execution, OBS
filter persistence, clean OBS shutdown, installer behaviour, and preservation
of user-owned files must be observed in the real desktop applications.

## Supported v1 hosts

The exact supported host versions are published in the
[v1 compatibility matrix](../compatibility/v1-matrix.md):

- Streamer.bot 1.0.4 stable;
- Streamer.bot 1.0.5-alpha.34;
- Streamer.bot 1.0.5-beta.1;
- Streamer.bot 1.0.5-beta.6;
- Streamer.bot 1.0.7 stable;
- OBS Studio 32.1.2 and 32.2.1 on Windows x64.

The OBS `32.x-windows-x64` profile is an internal compatibility family, not a
claim that every OBS 32.x release is supported. **OBS Studio 32.1.2 and 32.2.1
are the only exact OBS versions in the v1 support matrix.**

## Acceptance environments

Use at least one clean Windows 10 or later x64 machine or VM that has not run a
Foundry development build. Take a restorable snapshot before starting. Use
disposable Streamer.bot and OBS installations and disposable scenes; never use
a live production streaming configuration.

Record the machine, Windows build, Foundry package SHA-256, exact host versions,
and host installation paths in the [acceptance checklist](acceptance-checklist.md).
Do not record credentials, stream keys, OAuth tokens, or production channel
data.

## Evidence layers

### Automated evidence

Run the harness on the release commit:

```powershell
.\eng\release\invoke-final-acceptance.ps1 `
  -ProductVersion 1.0.0-rc.1 `
  -ObsRoot 'PATH_TO_DISPOSABLE_OBS_32.2.1' `
  -CleanMachineAttested
```

It invokes
the repository build and test suite and records a structured report conforming
to `schemas/product/foundry-final-acceptance-report-v1.schema.json`. The gate
covers model validation, deterministic builds, package goldens, Streamer.bot
mock-runtime profiles, OBS ABI inspection, and crash-isolated OBS source
create/destroy lifecycle tests. Retain the command output, report, and generated
compatibility results.

Automated Streamer.bot matrix cells use Foundry's mock runtime. They do not
launch Streamer.bot. Automated OBS lifecycle tests load the module in an
isolated helper process. They do not prove that the OBS GUI can save, restore,
and unload a real scene safely.

### Real GUI evidence

Complete both clean-machine workflows from the installed Foundry desktop:

1. Create a new Streamer.bot extension from a v1 template.
2. Edit, build, test, package, publish, deploy, import, compile, and execute it.
3. Repeat the runtime and health gate in every supported Streamer.bot version.
4. Exercise update, missing-file repair, rollback, modified-file protection,
   and uninstall.
5. Create a new OBS filter plugin from a v1 template.
6. Edit, build, test, package, publish, and deploy it to OBS Studio 32.2.1.
7. Attach the filter, restart OBS, verify persistence, remove the filter, and
   close OBS normally without a module error or crash report.
8. Exercise update, missing-file repair, rollback, modified-file protection,
   and uninstall while confirming Foundry blocks mutation when OBS is running.
9. Confirm both uninstallers preserve files that Foundry does not own.

Run the release build twice from identical inputs and compare every reported
archive SHA-256. A normal release timestamp can differ unless the deterministic
test path supplies the same build time; the fixed-time regression must be
byte-identical.

## Release decision

The release owner reviews the completed checklist, automated evidence, real-host
evidence, known issues, compatibility matrix, privacy statement, dependency
notices, and release notes. Shipping is blocked until:

- every required checklist item passes;
- no unresolved crash, data-loss, unsafe-uninstall, or modified-file issue
  remains;
- the product licence has been selected and the correct licence text is present;
- third-party notices have received a final dependency and legal review;
- the release archive and update manifest have been independently hash-verified.

The detailed build and publication procedure is in
[the v1 release runbook](../release/v1-release.md).
