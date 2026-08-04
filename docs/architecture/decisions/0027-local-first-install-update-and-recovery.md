# ADR 0027: Local-first install, update, and recovery

## Status

Accepted for Phase 14.

## Decision

Foundry is installed per user from a self-contained Windows package. Updates are
manual, manifest-driven, and staged only after SHA-256 and size verification.
Network access is disabled by default; local manifest, package, and OBS SDK
archive paths remain supported.

Crash and recovery evidence stays in local application data. Diagnostic bundles
are created only on request, redact local paths by default, and are never sent by
the application. Uninstall removes only receipt-owned product files and preserves
local state unless its explicit destructive option is selected.

## Consequences

- Clean machines do not require a preinstalled .NET runtime.
- An interrupted update can restore the previous application directory.
- Foundry can operate in offline or restricted environments.
- Creators retain control over diagnostic and local-path disclosure.
- Private-alpha distribution can reuse the same package/update contract.
