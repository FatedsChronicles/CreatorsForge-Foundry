# Safe OBS Studio deployment

Phase 10 gives OBS projects the same reviewed, recoverable deployment workflow
as Streamer.bot projects. Open **Build > Deploy / Manage Installation** from an
OBS project after a successful build.

## Discovery and process safety

Foundry discovers remembered installations, the standard Program Files
installation, and nearby folders containing `bin\64bit\obs64.exe`. An
installation can also be selected with **Browse**. Portable installations use
their local `config\obs-studio\logs` folder; standard installations use the
current user's OBS log folder.

Preview and apply are both blocked while the selected `obs64.exe` is running.
If Windows reports an OBS process but does not permit Foundry to inspect its
path, Foundry blocks the operation conservatively.

## Preview and apply

**Preview Install / Update** accepts only the verified `obsPluginPackage`
artifact declared by `build/package-ir.json`. The outer ZIP size and SHA-256
must match package IR, and the package's internal project ID, version, and
module name must match the open project.

Only these package paths can be installed:

```text
obs-plugins/64bit/<module-name>.dll
data/obs-plugins/<module-name>/...
```

Archive traversal, links, unrelated modules, oversized packages, duplicate
ownership, and linked destinations are rejected. Preview shows every create,
replace, restore, delete, and unchanged file. Apply requires the review
checkbox and a second confirmation, then rechecks source and destination
hashes to detect changes since preview.

## Receipts, backups, and ownership

Foundry stores provider-specific state inside the selected OBS installation:

```text
<OBS>/.foundry/obs/receipts/<projectId>.json
<OBS>/.foundry/obs/backups/<projectId>/<deploymentId>/
```

The receipt records the installed project and OBS versions, package hash,
owned paths and hashes, original files, immediate rollback files, and the
previous receipt. Its published contract is
[`schemas/deployment/obs-deployment-receipt-v1.schema.json`](../schemas/deployment/obs-deployment-receipt-v1.schema.json).

An update removes files that belonged to the previous package but are no
longer declared, while retaining enough receipt history to restore them during
rollback. A destination already owned by another active Foundry OBS receipt is
never replaced.

## Update, repair, rollback, and uninstall

- **Install / Update** writes the newly built DLL and module data after making
  recoverable backups.
- **Repair / Redeploy** is the same fully reviewed operation when health finds
  missing, modified, or package-drifted files.
- **Rollback** restores the immediately preceding file state and receipt.
- **Uninstall** restores files that existed before Foundry first managed the
  project, and removes only files Foundry originally created.

Rollback and uninstall stop when any managed destination is missing, modified,
or unexpectedly present. This prevents Foundry from deleting changes made by
the user, OBS, or another installer. Backup history is retained as recovery
evidence after rollback and uninstall.

## Health and OBS logs

**Check Health** compares the active receipt with:

- installed file existence, size, and SHA-256;
- the open project's version;
- the current built package hash;
- the OBS executable version used during installation;
- the newest OBS log written after deployment.

Health distinguishes not installed, invalid receipt, missing or modified
files, update available, installed version newer, package drift, host-version
change, module log failure, log not yet observed, and healthy. File integrity
failures take priority. A module-related OBS log failure is surfaced before
version advisories.

After applying a deployment, start and close OBS once, then select **Check
Health**. Foundry reports healthy only when every installed file verifies and a
post-install log mentions the module without a related load failure.

OBS deployment diagnostics use `CFOxxxx`:

- `CFO1xxx`: discovery, package, ownership, and preview validation;
- `CFO2xxx`: confirmation, state-change, apply, rollback, and uninstall
  failures;
- `CFO3xxx`: receipt and health inspection failures.

## Phase 10 exit gate

The product owner completed the full disposable-instance acceptance sequence
on 2026-07-27. Installation and health verification, update, rollback,
modified-file protection, repair, and uninstall all passed. Phase 10 is
accepted.

Use a disposable OBS instance to verify this sequence:

1. Build and preview an install with OBS closed.
2. Confirm the DLL and any data files in the preview, then apply it.
3. Start OBS, confirm the plugin behavior, close OBS, and obtain **Healthy**.
4. Build a newer project version, preview and apply the update, then verify it.
5. Modify a managed file and confirm health reports it and uninstall is blocked;
   restore it with the expected build before continuing.
6. Preview and apply rollback, then confirm the prior version and behavior.
7. Preview and apply uninstall, confirming pre-existing files are restored and
   Foundry-created files are removed.
8. Repeat a preview while OBS is running and confirm `CFO1009` blocks it.
