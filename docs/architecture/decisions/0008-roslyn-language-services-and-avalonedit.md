# ADR 0008: Use Roslyn language services with an AvalonEdit WPF surface

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Phase 4A needs syntax-aware C# editing, compiler diagnostics, formatting, and
source navigation without turning WPF controls into the source of semantic
truth. Editor results must match the extension's declared .NET Framework 4.8.1
and C# 7.3 build contract.

WPF's standard text box does not provide a code-editor rendering model. Roslyn
provides language semantics but not a WPF editor control.

## Decision

Use AvalonEdit `6.3.1.120` for the WPF text surface and
`Microsoft.CodeAnalysis.CSharp.Workspaces` `5.6.0` for language services.

Create a UI-independent `CreatorsForge.Foundry.Editor` project behind
`IRoslynEditorService`. For each operation, construct an in-memory Roslyn
project containing every source declared by `managedBuild.sources`. Configure
it with:

- C# language version 7.3;
- DLL output semantics;
- .NET Framework 4.8.1 reference assemblies;
- project source file paths for diagnostic and definition locations.

Copy the official .NET Framework reference assemblies into the application
output and create metadata references from those files. Cache the immutable
metadata references, but use short-lived `AdhocWorkspace` instances until
profiling justifies a persistent incremental workspace.

Debounce live analysis by 350 ms. Preserve Roslyn `CS` diagnostic codes and map
severity and one-based locations into `FoundryDiagnostic`. Keep formatting and
definition lookup asynchronous and cancellable.

Keep text ownership in `DocumentViewModel`. The AvalonEdit adapter synchronizes
with that model so Phase 3 dirty state, save, and recovery behavior remain
unchanged.

## Alternatives considered

- **WPF TextBox or RichTextBox:** No suitable syntax-rendering or code-editor
  primitives.
- **Build-only diagnostics:** Exact, but too slow and disruptive for live authoring.
- **Use Foundry's runtime assemblies as Roslyn references:** Smaller deployment,
  but would incorrectly validate .NET 10 APIs for a .NET Framework target.
- **Persistent Roslyn workspace immediately:** More efficient for large projects,
  but adds document lifecycle complexity before representative profiling data.
- **Implement a custom WPF text engine:** Large accessibility, rendering, input,
  and undo surface unrelated to Foundry's core value.

## Consequences

- Phase 4A adds Roslyn, AvalonEdit, and the .NET Framework 4.8.1 reference set
  to the desktop distribution.
- Diagnostics and builds share the same framework and language baseline.
- Editor semantics are testable without WPF.
- A later persistent workspace can retain the service contract.
- CPH completion and signature help remain separate Phase 4B capabilities.

## Validation

- Tests lock diagnostic codes and locations, C# 7.3 enforcement, formatting,
  and cross-file definition navigation.
- The Release build treats all compiler and analyzer warnings as errors.
- The desktop smoke gate opens the sample project and materializes its real
  AvalonEdit document surface.
