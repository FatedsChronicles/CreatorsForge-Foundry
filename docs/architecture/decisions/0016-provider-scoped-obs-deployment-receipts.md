# 0016: Use provider-scoped receipts for recoverable OBS deployment

Status: Accepted for Phase 10 on 2026-07-26.

## Context

OBS plugins consist of a native module and optional module data. Direct file
copying cannot identify ownership, preserve pre-existing files, detect later
modification, or safely distinguish update, rollback, and uninstall. OBS may
also hold native modules in process while it is running.

## Decision

OBS deployment consumes only the verified package declared by package IR and
allows only the module's DLL and data namespace. It uses a provider-scoped
state root under `.foundry/obs`, with one active receipt per project and
immutable deployment backup directories.

Every mutation follows preview and explicit confirmation. Source and
destination hashes are checked again at apply time. Modified or missing owned
files block destructive recovery operations. The selected OBS process must be
stopped for preview and apply. Health combines receipt/file verification with
the newest post-install OBS log.

## Consequences

- Pre-existing module files can be restored during rollback or uninstall.
- Foundry-created DLL and data files can be removed without scanning unrelated
  OBS directories.
- Multiple Foundry projects cannot claim the same active destination.
- Backup history consumes disk space until a future explicit cleanup feature.
- A healthy status requires one OBS launch after deployment so a relevant log
  exists.
- Process and log inspection are host-specific and remain separate from the
  deterministic build/package model.

