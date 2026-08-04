# 0018: Run OBS lifecycle tests in a crash-isolated process

Status: Accepted for Phase 11B on 2026-07-27.

## Context

Loading an untrusted native plugin into Foundry would let access violations,
loader faults, deadlocks, and invalid callback lifetimes terminate or hang the
desktop. Export inspection alone cannot prove source callback safety.

## Decision

Foundry first parses the plugin PE without loading it, verifies Windows x64 DLL
shape, and inventories required OBS exports. It then launches a dedicated .NET
native-test host with bounded JSON request/result files and a timeout.

The child loads the selected disposable OBS 32.x runtime, initializes the
module, verifies source registration, creates and releases the declared source,
and synchronizes with OBS's destroy queue before shutdown. Only a zero exit and
valid completed result can pass. Crashes, missing results, malformed output,
and timeouts become structured diagnostics in the parent.

## Consequences

- Native faults cannot terminate the CLI or future desktop Test Explorer.
- Lifecycle tests use real libobs semantics rather than an incomplete ABI shim.
- Tests require an explicit compatible disposable OBS installation.
- A malicious child can consume resources until timeout or OS limits intervene.
- The harness complements rather than replaces full OBS GUI verification.

