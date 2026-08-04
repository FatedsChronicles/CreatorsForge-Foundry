# 0021: Combine semantic goldens with byte-repeatability tests

Status: Accepted for Phase 11E on 2026-07-28.

## Context

Byte-equality alone can prove two runs agree while preserving the same wrong
contract. A complete checked-in native binary golden would instead be brittle
across an intentional pinned compiler upgrade. Foundry needs protection for
both package meaning and build reproducibility.

## Decision

Keep reviewed, human-readable semantic snapshots for representative
Streamer.bot stable-v23 and OBS package contracts. Lock provider metadata,
wire identities and links, decoded structure, archive layout, internal package
metadata, and fixed timestamps. Exclude toolchain-produced binary sizes and
hashes from these semantic snapshots.

Separately, build unchanged inputs twice and compare every produced package
artifact and package IR byte for byte. Create releases twice with a fixed UTC
time and compare both manifests and complete archives. Golden changes require
manual review and cannot be accepted automatically by a test switch.

## Consequences

- Semantic drift fails with a readable snapshot difference.
- Nondeterministic IDs, ordering, timestamps, compression, or metadata fail
  byte-repeatability checks.
- Intentional compiler upgrades do not require checking native binaries into
  source control.
- Package format changes require an explicit reviewed golden update and the
  relevant disposable-host runtime gate.
