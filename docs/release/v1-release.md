# Foundry v1 release runbook

This runbook turns an accepted release candidate into Foundry v1. It is a
reviewed, manual publication process. Foundry does not upload or publish itself.

## 1. Freeze the candidate

1. Choose the release commit and stop feature changes.
2. Confirm the worktree contains only intentional release changes.
3. Set the product version to `1.0.0` everywhere the packaging flow requires.
4. Finalize `CHANGELOG.md` and the dated v1 compatibility matrix.
5. Select and legally review the product licence, then add the correct licence
   file. **No v1 release may ship without this explicit choice.**
6. Review `THIRD-PARTY-NOTICES.md` against the resolved dependencies and actual
   self-contained publish output.

## 2. Produce automated evidence

From a clean repository checkout, run the complete automated final-acceptance
gate:

```powershell
.\eng\release\invoke-final-acceptance.ps1 `
  -ProductVersion 1.0.0-rc.1 `
  -ObsRoot 'PATH_TO_DISPOSABLE_OBS_32.1.2' `
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

Create the release candidate with the dedicated v1 packager and an explicit UTC
publication time:

```powershell
.\eng\release\package-v1.ps1 `
  -Version 1.0.0-rc.1 `
  -PublishedAtUtc '2026-07-29T00:00:00Z' `
  -OutputDirectory .\artifacts\v1-rc
```

Use a truthful timestamp for the candidate being built. Do not reuse an earlier
alpha archive. The release manifest must conform to
`schemas/product/foundry-v1-release-manifest-v1.schema.json`.

Independently verify the resulting manifest, archive names, versions, sizes,
and SHA-256 values:

```powershell
.\eng\release\verify-v1-release.ps1 `
  -ReleaseDirectory .\artifacts\v1-rc\CreatorsForge-Foundry-1.0.0-rc.1 `
  -ExpectedManifestSha256 '<separately supplied hash>'
```

Record the verified manifest SHA-256 separately from the distribution files.

After the release candidate passes every gate, build stable `1.0.0` from the
accepted release commit. Stable packaging refuses a missing product `LICENSE`,
missing commit identity, and unsigned release blockers. The
`-AllowUnsignedStable` override is exceptional and is **not recommended**; use
it only after the release owner records and approves why stable signing is
unavailable. It does not waive the product-licence gate.

If code signing is enabled, sign and verify the final executable/DLL payloads
before recording final hashes. A timestamped signature is intentionally not
byte-identical to an unsigned fixed-time package; retain signing evidence
separately from deterministic unsigned-build evidence.

## 4. Complete clean-machine acceptance

Transfer the exact candidate bytes to the clean acceptance machine, verify the
recorded hashes, install them, and complete every item in the
[final acceptance checklist](../final-acceptance/acceptance-checklist.md).

Real GUI verification is mandatory for all three exact Streamer.bot versions
and OBS Studio 32.1.2 Windows x64. Do not treat the Streamer.bot mock matrix or
the isolated OBS harness as a substitute. Retain sanitized logs and checklist
results without credentials or production channel information.

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

## 6. Approve and distribute

1. Have the release owner sign and date the completed checklist.
2. Preserve the release commit, build/test log, checklist, matrix, archive
   hashes, signing evidence if applicable, and reproducibility reports.
3. Copy only the accepted final archive, matching update manifest, privacy
   statement, product licence, notices, and release notes to the distribution
   location.
4. Run `eng/release/verify-v1-release.ps1` against the uploaded/downloaded files
   and retained manifest hash.
5. Point the update channel to the accepted manifest only after the package is
   available and hash-verifiable.
6. Install once from the distributed bytes, launch, check the reported version,
   and uninstall while confirming user data is preserved.

## 7. Rollback plan

Keep the preceding accepted installer/update manifest available until v1 has
been observed in distribution. If a release-blocking defect is found, remove or
disable the v1 update manifest, stop distribution, publish a clear advisory,
and use the receipt-backed product installer/uninstaller path. Never direct
users to delete broad application or user-data directories manually.
