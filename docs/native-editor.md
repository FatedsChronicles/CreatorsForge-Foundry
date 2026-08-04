# Native OBS editor

Phase 9B extends the AvalonEdit workspace with pinned, offline language help
for C17 OBS plugin sources. It deliberately uses the Phase 9A SDK contract
rather than silently consulting headers from an arbitrary OBS installation.

## Capabilities

- C/C++ syntax highlighting for `.c` and `.h` documents;
- automatic completion after `obs_` and `OBS_`, plus `Ctrl+Space`;
- function and macro signature help after `(` and `,`;
- parameter descriptions, owning header, compatibility, and official links;
- **Code > OBS Native API Reference** searchable local catalogue;
- live catalogue notices for unknown calls, profile-incompatible APIs, and a missing
  `<obs-module.h>` include;
- `F12` navigation from a catalogue symbol into the verified SDK header;
- read-only SDK header tabs, preventing accidental cache modification;
- native build diagnostics in Problems with `.c` and `.h` navigation.

The bundled `obs-libobs-32.1.2-v1` catalogue contains 35 high-value module,
source, filter, settings, property, rendering, lifetime, and localization
symbols. Every entry is limited to `32.x-windows-x64` and identifies the
matching 32.1.2 header. Completion and reference browsing therefore work
without network access and remain deterministic across machines.

## Diagnostic model

| Code | Meaning |
| --- | --- |
| `CFN1001` | Informational: an `obs_*` or `OBS_*` call is absent from the curated pinned catalogue. |
| `CFN1002` | A known symbol is not available for the selected target profile. |
| `CFN1003` | OBS APIs are used without including `<obs-module.h>`. |
| `CFN1004` | A verified SDK header could not be opened for navigation. |

These fast diagnostics are catalogue checks, not a replacement for the MSVC
compiler. **Build Project** remains authoritative for C syntax, types, linking,
and the complete header surface; its structured `Cxxxx` and `LNKxxxx`
diagnostics continue to flow into Problems.

## Header navigation trust boundary

Foundry resolves definitions only beneath
`<verified-sdk>/sources/libobs/`, only for `.h` files, and only while the SDK
integrity check is healthy. SDK tabs are read-only and excluded from autosave,
recovery, native analysis, and project persistence.

## Verification

Editor tests lock the catalogue revision, core filter inventory, prefix
completion, active-parameter selection, comment/string suppression, native
diagnostic locations, profile compatibility, and header mapping. The standard
Release gate additionally builds the WPF application and materializes the code
editor during its desktop smoke test.

The hands-on Phase 9B editor check passed on 2026-07-26: completion, signature
help, the searchable API reference, and SDK-header navigation worked as
expected.
