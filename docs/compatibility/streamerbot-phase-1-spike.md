# Streamer.bot Phase 1 compatibility spike

## Status

Offline compilation and in-process runtime verification are complete for all
three supplied installations. Each host invoked the probe DLL, passed arguments
across the bridge, completed the selected CPH calls, and loaded the secondary
dependency after both runtime DLLs were deployed beside `Streamer.bot.exe`.

## Supplied hosts inspected

| Profile | Product version | Executable configuration | Plugin interface target | Roslyn |
| --- | --- | --- | --- | --- |
| Stable | 1.0.4 | .NET Framework 4.7.2 | .NET Framework 4.8.1 | 4.14 |
| Alpha | 1.0.5-alpha.34 | .NET Framework 4.7.2 | .NET Framework 4.8.1 | 5.6 |
| Beta | 1.0.5-beta.1 | .NET Framework 4.7.2 | .NET Framework 4.8.1 | 5.6 |

The three `Streamer.bot.Plugin.Interface.dll` files have different SHA-256
hashes despite sharing assembly version 1.0.0.0. The bridge must therefore be
checked against each physical target rather than relying on assembly version
alone.

All three interfaces expose the required surface:

- `CPHInlineBase`
- `CPHInlineBase.CPH`
- `CPHInlineBase.args`
- `IInlineInvokeProxy.GetVersion()`
- `IInlineInvokeProxy.LogInfo(string)`
- `IInlineInvokeProxy.SetArgument(string, object)`

## Experiment design

The experiment consists of:

1. `CreatorsForge.Foundry.StreamerBot.CompatibilityProbe.dll`, a minimal
   `net481` library that accepts the argument dictionary and narrow delegates
   for host calls.
2. `CreatorsForge.Foundry.StreamerBot.DependencyProbe.dll`, a secondary
   `net481` assembly called by the main probe to test transitive dependency
   loading.
3. `CPHInline.cs`, a thin bridge that passes `args`, invokes `CPH.GetVersion`
   and `CPH.LogInfo`, publishes result arguments through `CPH.SetArgument`, and
   returns the probe result.

The bridge uses Streamer.bot's documented conditional external-editor pattern.
Its external branch inherits `CPHInlineBase`; its in-application branch is the
required global `CPHInline` class.

## Repeatable offline verification

From the repository root:

```powershell
.\eng\verify-streamerbot-hosts.ps1 `
  -StablePath "PATH_TO_STREAMERBOT_1_0_4" `
  -AlphaPath "PATH_TO_STREAMERBOT_1_0_5_ALPHA" `
  -BetaPath "PATH_TO_STREAMERBOT_1_0_5_BETA"
```

The script:

- validates the required host files;
- builds the bridge separately against each host's assemblies;
- produces profile-specific artifacts under
  `artifacts/streamerbot-compatibility`;
- records host versions and SHA-256 hashes in
  `compatibility-report.json`.

Generated artifacts are intentionally ignored by Git.

## Manual in-process verification

Perform these steps in a disposable Streamer.bot instance. Do not use a
production configuration or live channel.

1. Run the offline verification script.
2. Choose the artifact folder matching the instance under
   `artifacts/streamerbot-compatibility`.
3. Create a test action named `Creators Forge Foundry Compatibility Probe`.
4. Add an argument named `foundryProbeInput` with value `foundry-probe`.
5. Add an **Execute C# Code** sub-action.
6. Close Streamer.bot and copy these files from the matching artifact folder
   into the disposable instance directory beside `Streamer.bot.exe`:
   - `CreatorsForge.Foundry.StreamerBot.CompatibilityProbe.dll`
   - `CreatorsForge.Foundry.StreamerBot.DependencyProbe.dll`
7. Restart Streamer.bot.
8. Add the application-directory copy of
   `CreatorsForge.Foundry.StreamerBot.CompatibilityProbe.dll` as a compiler
   reference.
9. Paste the complete generated `CPHInline.cs` into the code editor.
10. Compile, save, and run the action.
11. Confirm the log contains a line beginning
   `Creators Forge Foundry probe: success=True`.
12. Confirm these output arguments:

| Argument | Expected value |
| --- | --- |
| `foundryProbeSuccess` | `True` |
| `foundryProbeInputObserved` | `foundry-probe` |
| `foundryProbeHostVersion` | The running Streamer.bot version |
| `foundryProbeDependency` | `foundry-dependency-loaded` |

Adding a DLL only in the code editor's References view is sufficient for
compilation but does not make that external directory a runtime probing path.
The first runtime attempt on each supplied version failed to resolve the main
probe until both DLLs were placed in the disposable instance's application
directory.

## Results

| Profile | Bridge compiles offline | Runs in host | Arguments pass | CPH calls work | Dependency loads |
| --- | --- | --- | --- | --- | --- |
| 1.0.4 stable | Yes | Yes | Yes | Yes | Yes |
| 1.0.5-alpha.34 | Yes | Yes | Yes | Yes | Yes |
| 1.0.5-beta.1 | Yes | Yes | Yes | Yes | Yes |

The product owner confirmed all three in-host tests passed on 2026-07-24.
Runtime deployment required both probe DLLs beside `Streamer.bot.exe`.

## Known limitations

- Package import/export compatibility is proven for the representative
  one-action fixture across the full three-by-three version matrix. Broader
  item types and semantic round trips remain later package-adapter work.
- The current proven deployment location is the Streamer.bot application
  directory. Foundry must preview and obtain confirmation before modifying that
  external location. A future explicit loader may permit a dedicated extension
  directory.
- The executable configuration and plugin-interface target-framework metadata
  disagree. The spike follows the plugin interface and official external-editor
  guidance by targeting .NET Framework 4.8.1.
- The experiment intentionally uses only a tiny CPH surface. Broader catalogue
  compatibility is a later phase.

## Primary documentation

- [Streamer.bot C# introduction](https://docs.streamer.bot/api/csharp/guide/intro)
- [Streamer.bot external-editor recipe](https://docs.streamer.bot/api/csharp/recipes/visual-studio-code)
- [Streamer.bot compiler references](https://docs.streamer.bot/api/csharp/guide/debugging)
