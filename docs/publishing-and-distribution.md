# Publishing and distribution

Phase 13 separates a convenient development release from a distribution-ready
publish operation. Publishing consumes only verified package-IR artifacts and
does not infer payloads by scanning the build directory.

## Project metadata

The optional `publishing` section of `.foundryproj` records the portable package
name, summary, authors, licence and changelog paths, web links, tags, declared
dependencies, and optional signing configuration. The desktop editor writes
metadata and version together through one validated atomic replacement.

Foundry combines declared dependencies with the target host, managed runtime
or pinned libobs SDK, and installed source components. The normalized inventory
is embedded in `foundry-build.json`.

## Checklist and outputs

`publish validate` performs a provider build and reports each required and
recommended check. `publish` repeats those gates and emits:

- the deterministic Streamer.bot or OBS provider archive;
- installation guidance and verified package IR;
- the declared licence and versioned changelog;
- `publishing-checklist.json`;
- `foundry-build.json` with dependencies, hashes, and signing evidence;
- a sibling reproducibility report containing the final archive SHA-256.

The schemas live under `schemas/packages`. Given identical verified inputs and
an identical injected build time, unsigned archives are byte-identical.

## Signing boundary

Signing is disabled by default. Enabling it requires a specific `signtool.exe`,
certificate thumbprint, and optional RFC 3161 timestamp URL. Foundry signs and
verifies distributable DLLs, including DLLs nested in the OBS package, before
recording payload hashes. It never searches for or chooses a certificate
automatically. Any signing or verification failure aborts publishing.

Publishing does not upload files or depend on a marketplace. Distribution is
an explicit creator action after reviewing the generated archive and report.
