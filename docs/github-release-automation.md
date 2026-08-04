# GitHub release automation

Foundry publishes native Windows release assets through the manually dispatched
**Publish Foundry Release** GitHub Actions workflow. The workflow must exist on
the repository's default branch before GitHub exposes **Run workflow**.

## Release assets

Every run builds, verifies, and attaches exactly these public release assets:

- `CreatorsForge-Foundry-<version>-Setup.exe`;
- `CreatorsForge-Foundry-<version>-Update.exe`; and
- `foundry-update.json`.

The portable ZIP remains a local packaging artifact and is not attached to the
GitHub Release. Setup and Update use the same native payload. The manifest names
the Update asset relatively and records its exact byte length and SHA-256 hash,
allowing the stable URL below to resolve both files from the same release:

```text
https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/latest/download/foundry-update.json
```

Foundry's **Stable** channel uses that URL. **Prerelease (includes stable)**
queries GitHub's public releases API, ignores drafts, considers only published
releases with an uploaded `foundry-update.json`, and selects the highest semantic
version. The selected manifest's relative updater path resolves within the same
immutable GitHub Release before the existing size and SHA-256 checks run.

## Running the workflow

After the workflow is merged into `main`:

1. Open the repository's **Actions** tab.
2. Select **Publish Foundry Release**.
3. Select **Run workflow** and choose `main`.
4. Enter semantic version text without `v`, such as `1.0.0`.
5. Select `draft`, `prerelease`, or `stable`.
6. Run the workflow and review its build, test, package, and verification steps.

`draft` is the default and safest first run. Draft releases do not become the
public latest release. `prerelease` publishes an alpha, beta, or release
candidate without moving the stable latest channel. `stable` rejects versions
containing a prerelease suffix and publishes the release as latest.

## Safety gates

The workflow:

- grants only `contents: write` to its `GITHUB_TOKEN`;
- serializes runs for the same version and never cancels an active release;
- rejects malformed versions, an existing release, or an existing tag;
- installs Inno Setup on an isolated Windows runner;
- runs the complete Release build, automated suite, and desktop smoke tests;
- compiles the native setup and updater;
- verifies filenames, version identity, size, SHA-256, identical installer
  payloads, Windows product metadata, and the official release-notes URL;
- retains the three verified files as a 30-day workflow artifact; and
- creates the GitHub Release and tag only after every preceding gate passes.

GitHub release assets are immutable inputs to Foundry's update channel. If a
version or tag already exists, investigate it and choose a new semantic version;
do not overwrite a published updater.

## Local verification

The same final asset check can run locally:

```powershell
.\eng\desktop\verify-desktop-release.ps1 `
  -Version 1.0.0 `
  -ReleaseDirectory .\artifacts\desktop
```

Code signing remains optional until a publisher certificate and protected
signing-secret policy are approved. The workflow does not contain or print a
certificate, private key, or signing password.

