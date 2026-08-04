# C# editor foundation

Phase 4A replaces the platform text box with an AvalonEdit document surface and
adds Roslyn-backed language services.

## Capabilities

- C# syntax highlighting and line numbers;
- spaces-for-tabs editing with four-space indentation;
- live compiler diagnostics after a 350 ms edit debounce;
- exact file, line, and column locations in Problems;
- double-click diagnostic navigation;
- source definition navigation with `F12`;
- Roslyn formatting with `Ctrl+Alt+F` or **Code > Format Document**;
- dirty tracking, save, autosave recovery, and document tabs inherited from
  the Phase 3 workspace.

The editor analyzes all C# files declared by `managedBuild.sources`, using the
unsaved text for open documents and the persisted text for closed documents.
Definition navigation therefore works across declared source files.

## Compatibility model

The first project schema fixes managed extensions to .NET Framework 4.8.1 and
C# 7.3. Editor parsing and diagnostics use that same language version and the
official .NET Framework 4.8.1 reference assemblies copied with the application.
Foundry's .NET 10 process assemblies are deliberately not used as compilation
references. This prevents the editor from accepting APIs that the extension
build cannot consume.

Roslyn diagnostics retain their `CS` identifiers and flow through the existing
`FoundryDiagnostic` model. Operational diagnostics and build diagnostics remain
in the same Problems surface.

## Architecture

`CreatorsForge.Foundry.Editor` is UI-independent and owns:

- construction of an in-memory Roslyn project;
- C# 7.3 parsing and .NET Framework 4.8.1 semantic compilation;
- diagnostic mapping;
- document formatting;
- source-symbol lookup.

`CreatorsForge.Foundry.App` owns the AvalonEdit control, keyboard interaction,
debouncing, tab selection, and navigation. The two layers communicate through
`IRoslynEditorService`.

Each operation currently creates a short-lived Roslyn `AdhocWorkspace`.
Reference metadata is cached for the process lifetime. This keeps state and
document replacement rules simple for the first editor gate; a persistent
incremental workspace can replace it if profiling shows a need on larger
projects.

## Verification

Editor-service tests cover:

- syntax error codes and locations;
- enforcement of C# 7.3;
- formatting;
- cross-file definition navigation.

The canonical `build.ps1` gate now launches the desktop in hidden smoke mode,
opens the sample `.foundryproj`, opens its declared C# source, materializes the
AvalonEdit control, and requires a clean exit.

## Phase 4B extension

The editor now also hosts the profile-aware CPH completion, signature help,
local reference, and compatibility diagnostics described in
[cph-catalogue.md](cph-catalogue.md).
