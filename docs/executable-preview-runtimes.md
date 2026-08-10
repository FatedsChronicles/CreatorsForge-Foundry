# Phase 22D executable provider preview runtimes

Phase 22D completes the original Stage 22 execution gate without weakening the
accepted structural-preview default. Open **View > Design Preview**, then opt in
with **Run live executable preview (untrusted code)**.

## Runtime adapters

| Provider content | Live execution | Output |
| --- | --- | --- |
| HTML/CSS/JavaScript | Disposable WebView2 profile over a bounded staged content tree | Captured live PNG |
| Streamer.bot WinForms | Deterministic build, copied managed DLL, isolated STA process and collectible load context | Captured live PNG |
| Applicable OBS component | Deterministic native build, copied plugin DLL, disposable libobs module/source/property lifecycle | Declared visual composition plus live lifecycle and property controls |

The OBS selector lists only supported 32.1.2 and 32.2.1 installations and
prefers an exact match for the project's declared API version.

Structural mode remains non-executing and is always the default.

## Isolation and limits

- Live mode is session-only and requires explicit opt-in.
- Source and artifact paths are confined before staging; reparse points are
  rejected.
- Web staging permits at most 128 supported files, 4 MiB per file, and 24 MiB
  total.
- Browser requests outside the staged local root, navigation, permissions, new
  windows, host objects, DevTools, context menus, autofill, and password saving
  are blocked.
- Live PNG output is validated and limited to 10 MiB; the complete result is
  limited to 16 MiB.
- Managed and native project code is never loaded by the Foundry desktop.
- The host retains timeout, process-tree termination, restart, stop, bounded
  logs, failure diagnostics, and disposable-directory cleanup.

Live execution is risk reduction, not a virtual-machine security boundary. Run
only code you trust.

## Phase 22D manual acceptance

1. Launch the Phase 22D desktop and open
   `samples/VisualPreviewSamples.foundryworkspace`.
2. Open **Creator Goal Overlay > Design Preview** and confirm structural mode
   remains the default.
3. Enable **Run live executable preview (untrusted code)** and confirm the
   adapter changes to `static-web-live-v1` and displays the real styled page.
4. Confirm the runtime log states that HTML, CSS, and JavaScript executed in a
   disposable WebView2 profile and that navigation/network/permissions are
   blocked.
5. Change visible HTML or CSS, save, and confirm live refresh. Change
   `overlay.js` to alter visible text, save, and confirm JavaScript executes.
6. Switch live mode off and confirm the safe structural adapter returns.
7. Activate **Streamer Control Panel**, enable live mode, and confirm Foundry
   builds the project and displays a captured real WinForms form using
   `winforms-live-v1`.
8. Change a control label or bounds, save, and confirm rebuild plus live refresh
   without loading the assembly into the Foundry desktop.
9. Activate **OBS Configurable Filter**, enable live mode, select the disposable
   OBS runtime, and confirm `obs-component-live-v1`.
10. Confirm the log reports module load, source registration/create/destroy,
    and the real properties callback. Confirm discovered controls appear in the
    properties composition.
11. Exercise Stop and Restart for all three providers and confirm Foundry stays
    responsive and retains the last completed frame.
12. Introduce a build error and confirm live execution does not start while the
    structural frame remains usable; then repair it and retry.
13. Close Design Preview during and after live refresh and confirm no
    PreviewHost, NativeTestHost, or WebView2 child process remains.
14. Confirm the disposable preview-runtime directory contains no completed run
    folders.

Phase 22D exited on 2026-08-06 after the complete automated gate and all
fourteen product-owner checks passed.
