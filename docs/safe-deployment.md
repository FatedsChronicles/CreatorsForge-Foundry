# Safe Streamer.bot deployment

Phase 7A adds a reviewed, recoverable deployment workflow for managed Foundry
extensions. It does not edit Streamer.bot configuration files or silently
import actions.

## Trust boundary

Open **Build > Deploy / Manage Installation**. Foundry discovers remembered
installations and nearby directories containing `Streamer.bot.exe`; another
folder can be selected explicitly.

Projects created before Phase 6 may not request `streamerBotPackage`. When that
is detected, the dialog offers **Enable Package Output**. After confirmation,
Foundry adds the output, creates a starter structured definition when needed,
reloads the workspace, rebuilds it, and returns to deployment preview.

Every mutating operation has two stages:

1. **Preview** validates the installation, package IR, artifact sizes and
   SHA-256 values, destination state, receipts, and backups. It displays each
   create, replace, restore, delete, or unchanged operation.
2. **Apply** requires both the review checkbox and a second confirmation. The
   engine also requires the exact preview fingerprint and rechecks source and
   destination hashes to prevent time-of-check/time-of-use replacement.

Deployment is blocked while the selected `Streamer.bot.exe` is running.

## Install and update

Foundry deploys the managed assembly beside `Streamer.bot.exe`, the runtime
probing location verified in Phase 1. It does not copy arbitrary package
contents. Only a `managedAssembly` whose size and hash match `package-ir.json`
is eligible.

Before replacement, Foundry stores recoverable copies beneath:

```text
<Streamer.bot>/.foundry/backups/<projectId>/<deploymentId>/
```

The active receipt is:

```text
<Streamer.bot>/.foundry/receipts/<projectId>.json
```

Receipts record the project and installation versions, installed file hashes,
the import-package hash, original backups, immediate rollback backups, and the
previous receipt. Their schema is
[`schemas/deployment/streamerbot-deployment-receipt-v1.schema.json`](../schemas/deployment/streamerbot-deployment-receipt-v1.schema.json).

After installation, use **Copy Import Code**, import it in Streamer.bot, add
the deployed DLL as the Execute C# compiler reference, compile, and run the
action. Streamer.bot's current export format does not provide a portable
machine-independent compiler-reference installation mechanism, so Foundry
keeps this host configuration step explicit.

## Rollback and uninstall

Rollback restores the immediately preceding file state and receipt. A first
installation can be rolled back to a pre-existing unmanaged DLL when one was
backed up.

Uninstall restores the file that existed before Foundry first installed the
project, or removes the deployed file if no original existed. Foundry refuses
rollback or uninstall when the active DLL no longer matches its receipt. This
prevents deleting user or third-party modifications.

Backups are retained after rollback or uninstall as recovery evidence. Foundry
does not automatically delete the `.foundry` history.

## Diagnostics

Deployment diagnostics use `CFDxxxx`:

- `CFD1xxx`: discovery, package, receipt, or preview validation;
- `CFD2xxx`: confirmation, state-change, application, or recovery failure.

## Deployment health and completion

Phase 7B adds **Check Health** to the deployment dialog. Selecting an
installation automatically compares:

- the active receipt and supported receipt schema;
- every installed DLL's existence, size, and SHA-256;
- the installed project version with the open project version;
- the current built import package with the package hash in the receipt;
- the current Streamer.bot file version with the host version last verified;
- the saved host-completion checklist.

Health states distinguish not installed, invalid receipt, missing files,
modified files, update available, installed version newer, package drift,
Streamer.bot version change, completion required, and healthy. The primary
button changes to **Preview Repair / Redeploy** or **Preview Update** when that
is the appropriate recovery action. Repairs still use the complete Phase 7A
preview, confirmation, backup, receipt, and time-of-check protections.

Completion is stored separately in each installation receipt:

1. package imported;
2. deployed DLL added as the Execute C# compiler reference;
3. imported code compiled;
4. action executed successfully at runtime.

When all four are saved, the receipt records `verifiedAtUtc`. If Streamer.bot
is later replaced or upgraded in that directory, health requires the compile
and runtime checks to be acknowledged again before returning to healthy.
