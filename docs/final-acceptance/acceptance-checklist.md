# Foundry v1 final acceptance checklist

Release candidate:                    Date:
Release commit:                       Tester/release owner:
Windows edition/build:                Clean machine or VM ID:
Desktop package SHA-256:              Update manifest SHA-256:

## Release prerequisites

- [ ] The candidate was built from the recorded clean commit.
- [ ] Release packaging recorded the commit identity and an explicit truthful
      UTC publication time.
- [ ] The product version is `1.0.0` in the executable, archive, update manifest,
      diagnostic bundle, and About/version surfaces.
- [ ] The product licence has been selected, reviewed, and added to the product
      and release package. This is a mandatory gate; this repository does not
      assume or invent a licence.
- [ ] `THIRD-PARTY-NOTICES.md` and the generated dependency inventory were
      reviewed against the actual shipped files.
- [ ] Privacy/offline behaviour and release notes were reviewed.
- [ ] `eng/release/verify-v1-release.ps1` independently accepted the release
      manifest and every recorded file hash and size.

## Automated release evidence

- [ ] `eng/release/invoke-final-acceptance.ps1` completed successfully and its
      report validates against
      `schemas/product/foundry-final-acceptance-report-v1.schema.json`.
- [ ] The complete repository build and automated test suite passed with no
      warnings or errors.
- [ ] Project schemas and machine-readable compatibility data parsed successfully.
- [ ] Streamer.bot mock-runtime tests passed for `1.0.4-stable`,
      `1.0.5-alpha.34`, and `1.0.5-beta.1`.
- [ ] OBS ABI inspection and crash-isolated source create/destroy lifecycle tests
      passed against the pinned 32.1.2 SDK/runtime.
- [ ] Streamer.bot and OBS golden-package regressions passed.
- [ ] Fixed-time repeated package/release builds were byte-identical.
- [ ] Self-contained desktop packaging, packaged-app smoke, and disposable
      install/uninstall automation passed.

These checks are automated evidence only. They do not replace the real GUI
checks below.

## Clean-machine desktop product

- [ ] Installation completed for a standard per-user account.
- [ ] First-run dependency checks and offline/privacy text were understandable.
- [ ] The app launched from the installed shortcut without a development SDK.
- [ ] Menus, hover states, dialogs, keyboard navigation, high contrast, and
      Unicode punctuation rendered correctly.
- [ ] Recovery restored a newer unsaved edit after the rehearsed interruption.
- [ ] A diagnostic bundle was created locally and contained no project source or
      recovery text.
- [ ] An offline session did not make an unexpected network request.
- [ ] The explicit update flow preserved projects, settings, and recovery state.

## Streamer.bot end-to-end workflow

- [ ] A new Streamer.bot extension was created from a bundled v1 template.
- [ ] Its C# was edited with completion, diagnostics, snippets, and formatting.
- [ ] Validate, Build, Test, Test Matrix, Package Viewer, Validate Publishing,
      and Publish Release completed successfully.
- [ ] The generated DLL, CPHInline bridge, import artifact, package manifest,
      licence, changelog, dependency inventory, and reproducibility report were
      present and internally verified.

Repeat the following real-host checks separately for each row. `Run Matrix`
uses a mock runtime and does not satisfy these rows.

| Exact host | DLL installed | Import accepted | Reference compiled | Action executed/logged | Health |
|---|---|---|---|---|---|
| Streamer.bot 1.0.4 stable | [ ] | [ ] | [ ] | [ ] | [ ] |
| Streamer.bot 1.0.5-alpha.34 | [ ] | [ ] | [ ] | [ ] | [ ] |
| Streamer.bot 1.0.5-beta.1 | [ ] | [ ] | [ ] | [ ] | [ ] |
| Streamer.bot 1.0.5-beta.6 | [ ] | [ ] | [ ] | [ ] | [ ] |
| Streamer.bot 1.0.7 stable | [ ] | [ ] | [ ] | [ ] | [ ] |

- [ ] An update installed and reported the new installed version.
- [ ] A deliberately missing Foundry-owned DLL was detected and repaired.
- [ ] A rollback restored the preceding healthy version.
- [ ] A deliberately modified DLL was detected and was not overwritten or
      removed without explicit review.
- [ ] Uninstall removed only Foundry-owned deployment files and receipts.
- [ ] Imported Streamer.bot configuration remained user-owned and was removed
      manually only from the disposable instance.

## OBS Studio end-to-end workflow

- [ ] A new OBS filter plugin was created from a bundled v1 template.
- [ ] Its native source was edited with completion, diagnostics, API reference,
      designer support, and lifecycle-safe create/destroy handling.
- [ ] Validate, Build, Test, Test Matrix, Package Viewer, Validate Publishing,
      and Publish Release completed successfully.
- [ ] The generated Windows x64 DLL, package data, manifest, licence, changelog,
      dependency inventory, and reproducibility report were present and verified.
- [ ] Real OBS Studio **32.1.2 Windows x64** loaded the installed module without
      a module-load error.
- [ ] Real OBS Studio **32.2.1 Windows x64** loaded the installed module without
      a module-load error.
- [ ] A real source accepted the filter, the filter remained attached after an
      OBS restart, and its settings persisted.
- [ ] Removing the filter and closing OBS completed without a crash report.
- [ ] The post-install OBS log contained no Foundry module error.
- [ ] Foundry refused install/update/repair/rollback/uninstall while OBS was running.
- [ ] An update installed and reported the new installed version.
- [ ] A deliberately missing Foundry-owned plugin DLL was detected and repaired.
- [ ] A rollback restored the preceding healthy version.
- [ ] A deliberately modified file was detected and protected.
- [ ] Uninstall removed only receipted Foundry files and left the disposable OBS
      installation and user-owned scene data intact.

OBS 32.1.2 and 32.2.1 are the only exact OBS versions supported for v1. Passing
an internal `32.x-windows-x64` test profile is not evidence for other OBS 32.x
releases.

## Final release decision

- [ ] The [v1 compatibility matrix](../compatibility/v1-matrix.md) matches the
      retained evidence and contains no broader support claim.
- [ ] No release-blocking crash, data-loss, unsafe-deployment, privacy, security,
      accessibility, or licensing issue remains open.
- [ ] Known non-blocking limitations are recorded in the release notes.
- [ ] The final distribution archive was installed once from the exact bytes to
      be distributed, then uninstalled without damaging user-owned files.

Result: **passed / passed with issues / blocked**

Release approval (name/date):

Evidence locations, issue references, and notes:
