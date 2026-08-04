# 0028: Invitation-only private alpha channel

## Status

Accepted for Phase 15.

## Decision

Private alpha releases are immutable `0.15.0-alpha.N` bundles delivered through
an access-controlled tester channel. The release manifest SHA-256 is delivered
separately in the invitation and the included verifier checks every asset before
installation. This is an integrity and channel-control model, not a claim that
an unsigned checksum establishes publisher identity. Authenticode remains an
optional strengthening when a publisher certificate is available.

Updates are explicit, hash-verified, and never installed silently. Diagnostics
remain local until a tester reviews and shares them. The release includes the
privacy statement, compatibility evidence, onboarding, issue template,
recovery rehearsal, acceptance checklist, and two complete sample projects.

## Consequences

- A compromised delivery channel and invitation channel together defeat the
  checksum trust model, so they must remain separate.
- Withdrawn versions are removed from the channel but retained releases are
  never altered.
- Private alpha evidence can be audited without adding accounts, telemetry, or
  a public marketplace.

