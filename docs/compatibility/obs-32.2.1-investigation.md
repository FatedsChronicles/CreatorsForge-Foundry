# Phase 17B — OBS Studio 32.2.1 compatibility investigation

## Status

Phase 17B is complete. Automated compatibility, the real OBS GUI gate, and the
complete recoverable deployment lifecycle passed. Module deployment, filter
attachment and persistence, clean restart/shutdown, log inspection, Healthy
status, process blocking, update, modified-file protection, repair, rollback,
and uninstall were product-owner verified on 2026-08-04.

The exact hosts are retained separately:

- `J:\Creators Forge\OBS-Studio-32.1.2` — product version 32.1.2;
- `J:\Creators Forge\OBS-Studio-32.2.1` — product version 32.2.1.

Folder names are not compatibility evidence. Foundry reads the version from
`bin\64bit\obs64.exe` and permits only exact verified runtime versions.

## SDK decision

Foundry continues to build with the pinned OBS 32.1.2 SDK. A module built with
that SDK passed ABI inspection and the crash-isolated module-load plus filter
source create/destroy lifecycle against both 32.1.2 and 32.2.1.

The Windows `obs.dll` export comparison found:

| Surface | 32.1.2 | 32.2.1 |
| --- | ---: | ---: |
| Exported symbols | 1,782 | 1,784 |
| Removed in 32.2.1 | — | 0 |
| Added in 32.2.1 | — | 2 |

The additions are `obs_source_get_dark_icon` and
`obs_source_get_light_icon`. Foundry templates do not require them. There is no
evidence that moving the build SDK pin is necessary for existing modules; a
future template that uses the new icon API must declare a 32.2.1 minimum.

## Release-change review

The official 32.2.1 release notes call out custom icons for new source types,
improved Windows DLL loading, shutdown crash fixes, missing-file support for
filters, and deprecation of `obs_properties_add_button`. The existing Foundry
filter lifecycle and property templates do not rely on removed APIs.

## Automated evidence

| Check | Result |
| --- | --- |
| Exact 32.2.1 executable identity | Passed |
| Windows x64 OBS executable and `obs.dll` fingerprint | Passed |
| 32.1.2 SDK build | Passed |
| Plugin PE/x64/required ABI exports | Passed |
| Isolated module load on 32.2.1 | Passed |
| Filter registration, create, and destroy on 32.2.1 | Passed |
| Retained 32.1.2 lifecycle regression | Passed |
| `obs.dll` export comparison | Passed — 2 added, 0 removed |
| Real module load and filter attachment | Passed |
| Filter persistence after restart | Passed |
| Clean filter removal and shutdown | Passed — no crash or unclean-shutdown warning |
| OBS log inspection and Foundry health | Passed — Healthy |
| Deployment process blocking | Passed |
| Update and installed-version comparison | Passed |
| Modified-file protection and repair | Passed |
| Rollback | Passed |
| Uninstall and receipt cleanup | Passed |

## Real-host acceptance

Using `J:\Creators Forge\OBS-Studio-32.2.1`:

1. Deploy the built `ObsPassthroughFilter` package through Foundry with OBS
   closed.
2. Start OBS and confirm no module-load failure for the Foundry module.
3. Add **Creators Forge Passthrough Filter** to a disposable source.
4. Save, close OBS cleanly, restart, and confirm the filter remains attached.
5. Remove the filter and close OBS; confirm no crash report or unclean-shutdown
   warning.
6. Return to Foundry, inspect the newest OBS log, and confirm Healthy.
7. Exercise update, modified-file protection, repair, rollback, and uninstall;
   verify user-owned scenes and sources remain.

All real-host checks passed. The exact 32.2.1 row is recorded in the main
compatibility matrix; compatibility is not implied for later OBS releases.
