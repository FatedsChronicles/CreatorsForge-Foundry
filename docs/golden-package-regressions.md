# Golden package and deterministic-build regressions

Phase 11E protects Foundry's two package contracts in complementary ways.

## Reviewed semantic goldens

The checked-in snapshots are:

- `tests/CreatorsForge.Foundry.Build.Tests/Golden/streamerbot-stable-v23-package.json`
- `tests/CreatorsForge.Foundry.Build.Tests/Golden/obs-module-load-v1-package.json`

The Streamer.bot snapshot locks the stable-v23 adapter version, counts,
round-trip result, decoded definition, deterministic wire IDs, reference links,
and embedded bridge hash. The OBS snapshot locks project/target metadata,
artifact layout, ZIP inventory, fixed entry timestamps, and the internal
`foundry-package.json` contract.

Compiler-produced binary sizes and hashes are deliberately not stored in the
semantic snapshots because they can change with an explicitly upgraded pinned
toolchain. Their integrity is still covered by package IR hashing and the
repeat-build tests.

## Byte-for-byte repeat builds

The regression suite builds unchanged inputs twice and compares:

- every Streamer.bot artifact, including the managed DLL, CPHInline bridge,
  import package, report, and package IR;
- the native OBS package ZIP and package IR;
- Streamer.bot and OBS release manifests and complete release ZIP bytes under
  an injected fixed UTC build time.

Together these checks detect unstable ordering, timestamps, IDs, compression,
metadata, line endings, and accidental machine-path leakage.

## Updating a golden

Goldens are review barriers, not self-updating test output. When an intentional
package-contract change is approved:

1. Explain the contract change in a new architecture decision or the decision
   that owns the format.
2. Update the generator and its focused tests.
3. Manually edit the affected golden snapshot to the reviewed new contract.
4. Run the focused build tests:

   ```powershell
   dotnet test .\tests\CreatorsForge.Foundry.Build.Tests `
     --configuration Release
   ```

5. Run `./build.ps1 -Configuration Release` and repeat the appropriate
   disposable Streamer.bot or OBS runtime acceptance gate.

Never add an automatic "accept current output" switch to the normal test run;
that would allow a regression to rewrite its own expected result.
