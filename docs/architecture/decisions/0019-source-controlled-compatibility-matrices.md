# 0019: Separate compatibility expectations from local runtime bindings

Status: Accepted for Phase 11C on 2026-07-27.

## Context

Regression coverage must be repeatable across both providers, but OBS runtime
locations are machine-specific while supported profiles are product policy.
Separate provider commands would also make result aggregation and future
desktop testing inconsistent.

## Decision

Test definitions store an ordered set of supported compatibility profiles.
A provider-neutral orchestrator selects the Streamer.bot mock adapter or the
crash-isolated OBS adapter. OBS installation roots are supplied at execution
time and never persisted in the project.

Every profile/runtime combination writes an independent structured test result.
Foundry also atomically writes a versioned aggregate matrix containing cell
identity, runtime version and path, outcome, diagnostics, and embedded result.

## Consequences

- CI, CLI, and the future desktop Test Explorer share one orchestration entry.
- Compatibility policy is reviewable in source control.
- Multiple disposable OBS installations can participate in one run.
- Missing local runtimes produce a structured matrix error instead of a crash.
- Streamer.bot profiles currently exercise the verified mock boundary; the
  earlier disposable-host runtime gates remain authoritative.
