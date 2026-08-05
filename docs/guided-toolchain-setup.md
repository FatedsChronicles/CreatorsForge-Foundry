# Guided development toolchain setup

Foundry discovers and validates the Visual Studio C++ x64 toolchain used for
OBS Studio projects. Open **Tools → Development Toolchain…** to review or change
the selected installation.

## Visual Studio discovery

Foundry first runs the Visual Studio Installer's `vswhere.exe` with the
`Microsoft.VisualStudio.Component.VC.Tools.x86.x64` workload requirement. This
supports current and future Visual Studio versions without assuming a fixed
year, edition, or installation directory. A bounded fallback checks the normal
Visual Studio roots under both `Program Files` and `Program Files (x86)`.

Every candidate must contain a complete `Hostx64/x64` toolset. Foundry selects
the newest installed MSVC tool version and verifies:

- `cl.exe`
- `link.exe`
- `lib.exe`
- `dumpbin.exe`
- `Common7/Tools/VsDevCmd.bat`

If discovery does not find the intended installation, choose **Select folder…**
and select the Visual Studio installation root—for example,
`C:\Program Files\Microsoft Visual Studio\2022\Community`. Incomplete roots are
explained and cannot be saved. **Auto-detect** refreshes the installer inventory.

## Persistence and build isolation

Choose **Save & Close** to persist the validated installation root in Foundry's
user settings. Reopening setup or the toolchain manager uses that exact
selection. OBS builds pass it to CMake as `CMAKE_GENERATOR_INSTANCE`; pinned SDK
generation invokes `dumpbin.exe` and `lib.exe` from the same validated toolset.

Foundry does not add anything to the user or machine `PATH` and does not modify
Visual Studio. Selection affects only Foundry-launched build operations. An
invalid or removed saved installation stops an OBS build with `CFB1011` and
directs the user back to the toolchain manager.

## Phase 21A manual acceptance

Passed on 2026-08-05 with Visual Studio Community 2022 and 2026. Automatic and
manual selection, persistence, invalid-root refusal, setup health, a real OBS
build, unchanged global PATH values, and the final readable dropdown label were
confirmed.

1. Launch Foundry and open **Tools → Development Toolchain…**.
2. Confirm installed Visual Studio instances with the C++ workload appear by
   product name and MSVC version, and that one reports **READY**.
3. Select a different valid instance if available, choose **Save & Close**,
   reopen the dialog, and confirm the selection persists.
4. Use **Select folder…** with a folder that is not a Visual Studio root and
   confirm Foundry explains that the C++ workload/tools are missing and does not
   save it.
5. Choose **Auto-detect** and confirm Foundry restores the discovered list.
6. Open **Tools → Run Setup Checks…** and confirm **Visual Studio C++ x64 tools**
   reports **READY** with the selected path.
7. Build an OBS sample and confirm CMake configures and compiles with the
   selected Visual Studio instance.
8. Confirm the Windows user and machine `PATH` values are unchanged.

Phase 21A exits when all eight checks pass without changing global environment
variables or regressing managed builds.

## Phase 21B consolidated readiness

The Development Toolchain window reports five independently actionable checks:

- CMake 3.20 or later, with automatic discovery or an exact `cmake.exe`
  selection.
- The persisted Visual Studio C++ Hostx64/x64 toolset.
- A complete Windows 10/11 SDK containing matching headers, x64 libraries,
  `rc.exe`, and `mt.exe`.
- Host-x64 to target-x64 architecture readiness.
- The checksum-verified pinned OBS SDK.

Each failed row includes a recommended action. **Open Visual Studio Installer**
opens the installed maintenance tool so the C++ workload or Windows SDK can be
added; **Get CMake…** opens the official CMake download page; **Refresh checks**
reruns every local inspection. These actions occur only after the corresponding
button is selected.

The saved CMake executable is passed directly to both CMake configure and build
operations. A removed, renamed, older, or invalid saved executable stops before
configuration with `CFB1012`. Foundry does not add CMake to PATH.

### Phase 21B manual acceptance

Passed on 2026-08-05. The Development Toolchain and first-run setup screens
visually confirmed CMake 4.4.2, Visual Studio Community 2026/MSVC 14.51.36231,
Windows SDK 10.0.26100.0 x64, native x64 targeting, and OBS SDK 32.1.2 as ready;
all guided actions, persistence, invalid-file refusal, build output, and PATH
boundaries also passed.

1. Open **Tools → Development Toolchain…** and confirm CMake, Visual Studio,
   Windows SDK, x64 architecture, and pinned OBS SDK each have a separate row.
2. Confirm this machine reports CMake 4.4.2, the selected Visual Studio
   installation, Windows SDK 10.0.26100.0 x64, and the pinned OBS SDK as ready.
3. Select the installed `cmake.exe`, save, reopen the dialog, and confirm the
   exact executable selection persists.
4. Try a disposable file named `cmake.exe` that is not CMake and confirm Foundry
   refuses it without replacing the saved valid selection.
5. Choose **Refresh checks** and confirm all readiness rows update without
   restarting Foundry.
6. Confirm **Open Visual Studio Installer** and **Get CMake…** open only when
   clicked; close them without changing the installed tools.
7. Run **Tools → Run Setup Checks…** and confirm the Windows SDK and x64 checks
   are included in the complete native-toolchain result.
8. Build the OBS Configurable Filter sample and confirm the DLL, package ZIP,
   and package IR are produced with the saved CMake and Visual Studio choices.
9. Confirm user and machine PATH values remain unchanged.

Phase 21B exits when all nine checks pass without regressing Phase 21A
selection or managed builds.

## Phase 21C disposable build verification

**Verify native build** performs a stronger check than file discovery. Foundry
creates a uniquely named workspace beneath the system temporary directory,
writes a minimal OBS module there, configures it with the selected CMake and
Visual Studio generator instance, links it against the pinned OBS SDK, verifies
the expected x64 DLL, and removes the workspace. The open project is never read
or changed by this operation.

The result lists each timed stage and reports actionable diagnostics:

- `CFB1101` when readiness blocks the run.
- `CFB1102` when CMake configuration fails.
- `CFB1103` when native compile or link fails.
- `CFB1104` when the expected DLL is absent.
- `CFB1105` when the disposable run cannot be prepared or executed.
- `CFB1106` when temporary cleanup needs attention.

Configure and compile failures include the command, exit code, captured output,
and a repair action. **Use recommended tools** selects the newest ready Visual
Studio toolchain and discovered CMake executable; the individual selectors
remain available when a different installation is required. Nothing is
persisted until **Save & Close** is chosen.

### Phase 21C manual acceptance

Passed on 2026-08-05. Product-owner runtime verification confirmed every
disposable native-build stage, including configuration-specific probe discovery
and owned-workspace cleanup. The corrected result dialog contains readable
ASCII separators. The full automated and desktop smoke gates cover the
provider sample regressions and unchanged managed workflow.

1. Build and launch the Phase 21C desktop from the acceptance location.
2. Open **Tools > Development Toolchain...** and confirm all five readiness rows
   report **READY**.
3. Choose **Verify native build** and confirm readiness, workspace preparation,
   CMake configure, native compile/link, DLL inspection, and cleanup all pass.
4. Confirm the message reports **Native build verification passed** and no
   `creators-forge-toolchain-probe.dll` remains in the open project.
5. Choose **Use recommended tools**, save, reopen the dialog, and confirm the
   selected Visual Studio and CMake values remain ready.
6. Select or simulate an invalid tool, confirm verification is blocked with
   clear repair guidance, then restore the valid selection and pass again.
7. Build the OBS Configurable Filter sample and confirm its DLL, package ZIP,
   and package IR are still produced.
8. Build the Streamer.bot sample and confirm the managed build is unchanged.
9. Confirm user and machine `PATH` values remain unchanged.

Phase 21C exited after product-owner runtime verification and the complete
regression gate passed.
