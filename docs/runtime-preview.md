# Phase 22B isolated runtime preview

Phase 22B adds a generic rendering host and refresh lifecycle to the safe
structural frame introduced in Phase 22A. Open **View > Design Preview...** or
press **Ctrl+Shift+P**.

## Runtime boundary

Foundry first performs the existing bounded source analysis. It then writes a
JSON request containing only the project identifier, viewport, source hash,
and sanitized visual elements. `CreatorsForge.Foundry.PreviewHost` runs in a
separate process and returns a styled visual frame.

The generic Phase 22B host:

- receives no project assembly path;
- cannot load or execute project binaries through its protocol;
- receives no JavaScript or complete HTML, C#, or C source text;
- accepts no more than 48 visual elements and a 3840x2160 viewport;
- is terminated with its process tree after eight seconds;
- limits captured output and result files;
- removes per-run request and result files after Foundry reads them.

Actual browser, WinForms assembly, and OBS plugin adapters remain Phase 22C
work. Phase 22B establishes the isolation and lifecycle they must use.

## Refresh and recovery workflow

- **Refresh preview** re-analyzes the selected source and starts a fresh host
  generation.
- **Refresh automatically when the source is saved** watches only the selected
  source and debounces duplicate file-system notifications for 650 ms.
- **Stop host** cancels the active generation and preserves the last completed
  or structural frame.
- **Restart host** cancels the current generation, re-analyzes the source, and
  starts the next numbered generation.
- **Runtime log** shows bounded host output and the isolation notice.

Lifecycle status is reported as Stopped, Starting, Running, Completed, Failed,
or TimedOut. Failures never close the Foundry editor.

## Diagnostics

- `CFW2310`: the installed preview-host assembly is missing;
- `CFW2311`: the host could not be started or run;
- `CFW2312`: the host exceeded the configured timeout;
- `CFW2313`: the host crashed or returned an invalid frame.

## Phase 22B manual acceptance

1. Launch the Phase 22B desktop and open the OBS Configurable Filter sample.
2. Press **Ctrl+Shift+P** and confirm the runtime state moves through Starting,
   Running, and Completed.
3. Confirm the surface title contains `isolated obs-component`, the OBS canvas
   and template badge remain visible, and generation 1 is reported.
4. Expand **Runtime log** and confirm it reports a bounded frame and states that
   project assemblies and scripts were not loaded.
5. Choose **Restart host** and confirm the next generation completes without
   closing or freezing Foundry.
6. Choose **Stop host** and confirm the last completed frame remains visible.
7. Open a disposable static-web preview, leave automatic refresh enabled, edit
   and save its selected HTML source, and confirm one debounced refresh occurs.
8. Disable automatic refresh, save the source again, and confirm the frame does
   not change until **Refresh preview** is selected.
9. Change the viewport and confirm the refreshed role-aware frame uses the new
   dimensions.
10. Close the preview during or after a refresh and confirm Foundry remains
    responsive and no preview-host process remains running.

Phase 22B exited on 2026-08-05 after the automated regression gate and all ten
product-owner acceptance checks passed, including the completed-frame Stop
workflow and readable deployment installation selectors.
