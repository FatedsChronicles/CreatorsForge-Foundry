# Foundry v1 release runbook

This runbook turns an accepted release candidate into Foundry v1. It is a
reviewed, manual publication process. Foundry does not upload or publish itself.

## 1. Freeze the candidate

1. Choose the release commit and stop feature changes.
2. Confirm the worktree contains only intentional release changes.
3. Confirm the stable product version is `1.0.0` everywhere the packaging flow
   requires it.
4. Finalize `CHANGELOG.md`, `docs/release/v1.0.0.md`, and the dated v1
   compatibility matrix.
5. Review the approved product EULA in root `LICENSE.md`. **No v1 release may
   ship without that exact file in the installer and release bundle.**
6. Review `THIRD-PARTY-NOTICES.md` against the resolved dependencies and actual
   self-contained publish output.

## 2. Produce automated evidence

From a clean repository checkout, run the complete automated final-acceptance
gate:

```powershell
.\eng\release\invoke-final-acceptance.ps1 `
  -ProductVersion 1.0.0 `
  -ObsRoot 'PATH_TO_DISPOSABLE_OBS_32.2.1' `
  -OutputDirectory .\artifacts\final-acceptance `
  -CleanMachineAttested
```

Retain the output and the report conforming to
`schemas/product/foundry-final-acceptance-report-v1.schema.json`. Confirm zero
warnings/errors, all tests passing, valid JSON schemas and compatibility
documents, package goldens passing, and fixed-time deterministic-build
comparisons passing.

These checks simulate Streamer.bot and isolate OBS native lifecycle execution.
They do not complete the real GUI acceptance gate.

## 3. Build the desktop candidate

Create stable 1.0.0 from the accepted clean release commit with the dedicated
v1 packager and an explicit truthful UTC publication time:

```powershell
.\eng\release\package-v1.ps1 `
  -Version 1.0.0 `
  -PublishedAtUtc '2026-08-10T00:00:00Z' `
  -OutputDirectory .\artifacts\v1-release
```

Use the real publication timestamp; the example above must be replaced if the
release is published later. Do not reuse an earlier alpha archive. The release
manifest must conform to
`schemas/product/foundry-v1-release-manifest-v1.schema.json`.

Independently verify the resulting manifest, archive names, versions, sizes,
and SHA-256 values:

```powershell
.\eng\release\verify-v1-release.ps1 `
  -ReleaseDirectory .\artifacts\v1-release\CreatorsForge-Foundry-1.0.0 `
  -ExpectedManifestSha256 '<separately supplied hash>'
```

Record the verified manifest SHA-256 separately from the distribution files.

Stable packaging refuses a missing product `LICENSE.md`, a dirty tracked
worktree, missing commit identity, and unsigned release blockers. The
`-AllowUnsignedStable` override is exceptional; use it only after the release
owner records and approves why publisher signing is unavailable. It never
waives the product-licence, clean-source, identity, hash, or acceptance gates.

If code signing is enabled, sign and verify the final executable/DLL payloads
before recording final hashes. A timestamped signature is intentionally not
byte-identical to an unsigned fixed-time package; retain signing evidence
separately from deterministic unsigned-build evidence.

## 4. Complete clean-machine acceptance

Transfer the exact candidate bytes to the clean acceptance machine, verify the
recorded hashes, install them, and complete every item in the
[final acceptance checklist](../final-acceptance/acceptance-checklist.md).

Real GUI verification is mandatory for all five exact Streamer.bot versions
listed in the compatibility matrix and OBS Studio 32.1.2 plus 32.2.1 Windows
x64. Do not treat the Streamer.bot mock matrix or isolated OBS harness as a
substitute. Retain sanitized logs and checklist results without credentials or
production channel information.

## 5. Review release blockers

The release owner must stop publication for any unresolved:

- crash, unsafe native unload, or data loss;
- overwrite/removal of a modified or user-owned file;
- failure to update, repair, roll back, or uninstall safely;
- mismatch between package, manifest, executable, and advertised version;
- unsupported or ambiguous host compatibility claim;
- privacy, security, accessibility, licence, or dependency-notice issue;
- non-reproducible fixed-time build or unexplained artifact difference.

Document accepted non-blocking limitations in the release notes.

## 6. Approve and publish

1. Have the release owner sign and date the completed checklist.
2. Preserve the release commit, build/test log, checklist, matrix, archive
   hashes, signing evidence if applicable, and reproducibility reports.
3. Merge the accepted release-readiness pull request into `main` and confirm
   the merge commit is the exact source revision approved by the evidence.
4. Run **Publish Foundry Release** on `main` with version `1.0.0` and release
   type `stable`. The current hosted workflow has no publisher certificate, so
   enable **Approve unsigned stable release** only after the release owner has
   explicitly recorded acceptance of the Windows unknown-publisher warning.
5. The workflow must create tag `v1.0.0` from the accepted `main` commit and
   upload the setup executable, updater executable, update manifest, portable
   archive, v1 release bundle, and independently recordable manifest hash.
6. Run `eng/release/verify-v1-release.ps1` against the uploaded/downloaded files
   and retained manifest hash.
7. Point the update channel to the accepted manifest only after the package is
   available and hash-verifiable.
8. Install once from the distributed bytes, launch, check the reported version,
   and uninstall while confirming user data is preserved.

## 7. Rollback plan

Keep the preceding accepted installer/update manifest available until v1 has
been observed in distribution. If a release-blocking defect is found, remove or
disable the v1 update manifest, stop distribution, publish a clear advisory,
and use the receipt-backed product installer/uninstaller path. Never direct
users to delete broad application or user-data directories manually.
