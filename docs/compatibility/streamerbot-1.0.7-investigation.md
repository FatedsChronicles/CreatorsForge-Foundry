# Phase 24A - Streamer.bot 1.0.7 stable investigation

Phase 24A validates the official Streamer.bot 1.0.7 stable Windows x64 release
before Creators Forge Foundry enters the stable v1.0.0 release gate.

## Authoritative release and isolated host

- Official release: `Streamer.bot-x64-1.0.7`, published 2026-08-06.
- Official archive SHA-256:
  `47a4e56f7a8a3e5f09ec16a5ba14c50e034dd98d67d2dc1ec89a671994c1f5c5`.
- Exact executable product version: `1.0.7`.
- Foundry profile: `1.0.7-stable`.

The portable archive is hash-verified before extraction. Verification uses a
disposable host and never a production Streamer.bot configuration.

## CPH interface result

`Streamer.bot.Plugin.Interface.dll` has SHA-256
`aa6d8eeffa06eeb7f3e62bc6e296ce2301b67fcfdf538e46dc7676feb202bbbc`.
The generated inventory contains 512 methods and 564 overloads. It has no
public method or overload additions or removals relative to 1.0.5-beta.6.

## Automated gate

Run the exact-host bridge check with all retained Streamer.bot installations:

```powershell
.\eng\verify-streamerbot-hosts.ps1 `
  -StablePath "PATH_TO_1.0.4" `
  -AlphaPath "PATH_TO_1.0.5_ALPHA_34" `
  -BetaPath "PATH_TO_1.0.5_BETA_1" `
  -Beta6Path "PATH_TO_1.0.5_BETA_6" `
  -Stable107Path "PATH_TO_1.0.7"
```

The release gate also requires the complete solution tests, five-profile mock
matrix, deterministic package regression, and current OBS 32.2.1 ABI and
source create/destroy lifecycle checks.

## Required real-host acceptance

In the disposable 1.0.7 host:

1. Create or open a representative Streamer.bot extension targeting
   `1.0.7-stable`.
2. Build, run its test and five-profile matrix, and inspect the package.
3. Preview and install it into the disposable host.
4. Import the generated code, add the installed DLL as a compiler reference,
   compile, and run the action.
5. Confirm the Foundry log message and Healthy deployment state.
6. Exercise update, modified-file protection, repair, rollback, and uninstall.
7. Confirm uninstall removes Foundry-owned DLL/receipt files while preserving
   user-owned Streamer.bot data.

## Status

Automated compatibility passed on 2026-08-10:

- the managed probes and generated bridge compiled against all five exact
  Streamer.bot installations with zero warnings or errors;
- the representative extension built and produced a valid stable-v23 package;
- its four tests and all five mock-runtime matrix cells passed;
- publishing validation passed;
- the complete solution gate passed 295 tests and all managed, native, and
  multi-project desktop smoke cases with a zero-warning release build.

The real 1.0.7 GUI host gate passed on 2026-08-10. Installation, import,
compiler reference, compilation, action execution/logging, Healthy status,
update, modified-file protection, repair, rollback, and uninstall were
product-owner verified. OBS Studio 32.2.1 manual acceptance also passed. The
exact versions are recorded in `v1-matrix`.

The follow-up desktop acceptance also passed. Newly created projects expose
the 1.0.7 profile immediately, display friendly template names, populate
publishing authors, generate editable MIT and changelog files, validate cleanly
after build, and wrap prose documents. Phase 24A is complete.
