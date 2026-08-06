# Phase 22A design preview foundation

Phase 22A introduces an explicitly enabled, non-running design surface for
projects with visual content. Open **View > Design Preview...** or choose
**Preview** on the main toolbar.

## Project contract

The optional `preview` object records:

- `enabled`: whether the design surface is enabled;
- `kind`: `static-web`, `winforms`, or `obs-component`;
- `source`: the single project-relative source inspected by the designer;
- `width` and `height`: a viewport from 240x180 through 3840x2160.

Removing the declaration disables preview without changing any source file.
The designer offers only compatible candidates: `.html` project content,
declared managed `.cs` sources for WinForms, or the source declared by
`obsPlugin.design`.

## Safety boundary

Phase 22A is a structural design view, not a runtime host. It:

- confines source resolution to the project root;
- reads at most 1 MiB from one explicitly declared source;
- parses a bounded maximum of 48 recognized elements with regex timeouts;
- hashes the inspected UTF-8 source for traceability;
- never loads a project DLL;
- never starts a browser or executes HTML, JavaScript, C#, or native code.

Static web previews show recognized visible HTML structure and deliberately
ignore scripts. WinForms previews recognize supported control declarations,
text, locations, and sizes without compiling them. OBS previews visualize the
persisted component and template metadata without loading the plugin.

Phase 22B passes this sanitized structural frame to a crash-isolated host.
Phase 22C selects a bounded provider adapter for static web, WinForms, or OBS
composition while preserving the same no-code-execution boundary; see
[provider-preview-adapters.md](provider-preview-adapters.md).

## Diagnostics

- `CFP0068`-`CFP0072`: invalid preview kind, source, viewport, or provider
  eligibility.
- `CFW2301`: preview is disabled.
- `CFW2302`: source resolution escaped the project boundary.
- `CFW2303`: declared source is missing.
- `CFW2304`: source exceeds the 1 MiB limit.
- `CFW2305`: bounded source inspection failed.
- `CFW2306`: preview settings could not be persisted.

## Phase 22A manual acceptance

1. Launch the Phase 22A desktop and open the OBS Configurable Filter sample.
2. Open **View > Design Preview...** and confirm `OBS component structure` and
   `src/plugin.c` are selected automatically.
3. Confirm the surface shows the OBS component and template without building or
   loading the plugin.
4. Change between HD, Full HD, Compact, Portrait, and Custom viewport sizes and
   confirm the surface resizes.
5. Choose **Save & Close**, reopen the designer, and confirm the preview kind,
   source, and viewport persist in the `.foundryproj` file.
6. In a disposable Streamer.bot project, create `ui/index.html`, add visible
   header, main, and button elements plus a script, then choose `Static web
   structure` and refresh.
7. Confirm visible elements appear while the script is neither displayed as a
   component nor executed.
8. Try a missing or parent-relative source and confirm Foundry reports a stable
   diagnostic without leaving the project.
9. Disable preview, save, and confirm the optional `preview` declaration is
   removed without changing the declared source.
10. Build both provider samples and confirm preview metadata does not affect
    their build or package outputs.

Phase 22A exits when the complete automated gate and these ten checks pass.
