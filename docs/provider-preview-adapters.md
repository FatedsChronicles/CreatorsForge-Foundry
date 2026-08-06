# Phase 22C provider-specific preview adapters

Phase 22C builds three visual adapters on the crash-isolated lifecycle created
in Phase 22B. Open `samples/VisualPreviewSamples.foundryworkspace`, activate a
sample, and press **Ctrl+Shift+P**.

## Adapter contract

Structural analysis emits one bounded adapter descriptor with an ID, display
name, and at most 12 short metadata values. The PreviewHost validates this
descriptor before choosing an adapter. It still receives no project binary
path and no complete source text.

| Preview kind | Adapter | Visual composition |
| --- | --- | --- |
| `static-web` | `static-web-v1` | Safe browser-like chrome, scripts-blocked badge, and semantic web roles |
| `winforms` | `winforms-v1` | Form title/client chrome and recognized native-control roles |
| `obs-component` | `obs-component-v1` | OBS toolbar, program canvas, component frame, and properties panel |

Unknown or older descriptors fall back to `generic-v1`, preserving backward
compatibility with Phase 22B frames.

## Execution boundary

These are provider-specific isolated design adapters. They do not:

- start WebView or another browser engine;
- execute HTML scripts or follow external navigation;
- compile or load a managed project assembly;
- initialize WinForms runtime objects from project code;
- initialize libobs;
- load an OBS plugin DLL.

This gives creators provider-recognizable visual feedback while preserving the
crash and trust boundary already accepted in Phase 22B.

## Representative samples

- `VisualWebOverlay`: Creator Goal Overlay with HTML, CSS, and deliberately
  unexecuted JavaScript;
- `VisualWinFormsPanel`: buildable Streamer Control Panel with labels, inputs,
  checkbox, progress bar, and action buttons;
- `ObsConfigurableFilter`: lifecycle-safe OBS filter with explicit preview
  metadata.

`VisualPreviewSamples.foundryworkspace` opens all three together.

## Phase 22C manual acceptance

1. Launch the Phase 22C desktop and open
   `samples/VisualPreviewSamples.foundryworkspace`.
2. Activate **Creator Goal Overlay**, press **Ctrl+Shift+P**, and confirm the
   adapter reads `static-web-v1`.
3. Confirm browser-like chrome, a **Scripts blocked** badge, semantic content,
   and the `Creator Goal Overlay` document title appear.
4. Save a visible text change in `ui/index.html` and confirm the debounced frame
   refreshes; confirm `overlay.js` is not executed.
5. Activate **Streamer Control Panel** and confirm `winforms-v1`, the form title,
   client surface, labels, input, checkbox, progress bar, and buttons appear.
6. Change and save a control label or bounds and confirm the isolated preview
   refreshes without loading the managed assembly.
7. Build both managed visual samples and confirm each produces its managed DLL
   and package IR without warnings or errors.
8. Activate **OBS Configurable Filter** and confirm `obs-component-v1`, the OBS
   toolbar, program canvas, component frame, template badge, and properties
   panel appear.
9. Expand Runtime log and confirm it states that browser engines, project
   assemblies, scripts, libobs, and native plugins were not loaded.
10. Exercise Restart and Stop for each adapter and confirm the last completed
    frame remains visible after Stop.
11. Close Design Preview during or after refresh and confirm no PreviewHost
    process remains running.
12. Build the existing OBS sample and confirm preview metadata does not change
    its native package inputs or lifecycle tests.

Phase 22C exits after the automated regression gate and all twelve checks pass.

Product-owner manual acceptance passed all twelve checks on 2026-08-06.
