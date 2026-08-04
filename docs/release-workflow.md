# Unified release workflow

The unified release milestone adds one validated release operation for
Streamer.bot extensions and OBS Studio plugins. It consumes the existing package intermediate
representation instead of discovering arbitrary files in `build`.

Run it from the desktop with **Build > Build Release Package**, or from the
CLI:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  release .\samples\HelloFoundry\HelloFoundry.foundryproj
```

The operation validates the current project, performs a Release build,
verifies every package-IR artifact by size and SHA-256, assembles the release
directory, creates the archive, and reads the archive back to verify its entry
inventory and hashes. Missing, modified, duplicated, rooted, or escaping
artifact paths stop release creation.

## Output layout

For project ID `com.example.extension` at version `1.2.3`:

```text
build/release/
├── com.example.extension-1.2.3/
│   ├── README.md
│   ├── foundry-build.json
│   ├── package-ir.json
│   └── <verified target artifacts at their package-IR paths>
└── com.example.extension-1.2.3-foundry.zip
```

`README.md` contains profile-specific installation and runtime verification
instructions. Streamer.bot releases describe DLL references, import code, and
the generated CPH bridge. OBS releases require installation into a disposable
host, a clean module log, persistence after restart, and a clean shutdown.

`foundry-build.json` follows
`schemas/packages/release-manifest-v1.schema.json`. It records the Foundry
version, UTC build time, Release configuration, target contract, warnings,
validation gates, dependencies, and the size and SHA-256 of every payload
file. The manifest deliberately does not hash itself, avoiding a circular
inventory.

ZIP entries use sorted portable paths and a normalized 1980 timestamp. Payload
content is deterministic when the underlying build and injected build time are
the same. The build timestamp remains truthful and therefore changes between
normal release runs.

Release validation proves provenance and integrity; it does not claim that
extension code is safe. Native OBS plugins must still pass the disposable-host
runtime gate, and Streamer.bot extensions must still pass the installation
health checklist.

## Publishing a distributable release

The ordinary release command remains useful for development hand-offs. Public
or tester distribution uses the stricter publishing flow. In the desktop, open
**Build > Publishing Metadata**, then use **Validate Publishing** and **Publish
Release**. The matching commands are:

```powershell
foundry publish validate .\MyProject.foundryproj
foundry version .\MyProject.foundryproj patch
foundry publish .\MyProject.foundryproj
```

Publishing requires a portable package name, summary, author, a non-empty
licence file, a non-empty changelog that names the exact project version, a
dependency inventory, and a verified provider archive. Each published bundle
adds the licence, changelog, and `publishing-checklist.json`; its release
manifest records normalized dependencies and signing status.

An external `<package>-reproducibility.json` report records the archive hash,
archive size, build-manifest hash, build time, and reproduction command. It
sits beside the archive so it can describe the finished archive without a
circular hash.

Code signing is optional. When enabled, Foundry requires a configured Windows
SDK `signtool.exe` and a certificate thumbprint in the current-user store.
Loose DLLs and DLLs inside provider ZIPs are signed and verified before final
hashes are recorded. Signing failure stops publishing.
