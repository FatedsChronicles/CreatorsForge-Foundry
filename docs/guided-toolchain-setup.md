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
