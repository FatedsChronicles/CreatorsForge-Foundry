# 0022: Record versioned project-template provenance

Status: Accepted for Phase 12A on 2026-07-28.

## Context

Provider-only project creation cannot distinguish a minimal module from a
filter, input, output, or Streamer.bot workflow. Future template upgrades also
need to know what Foundry generated without inferring lineage from source code.

## Decision

Ship a versioned built-in catalogue spanning Streamer.bot extension/command
workflows and OBS module/filter/input/output components. Capture author and
description through the New Project form. Persist the selected template ID,
revision, and parameter values as optional schema-v1 manifest provenance.

Generated OBS component templates own their callback context through paired
create/destroy functions. The encoded output template pairs begin/end capture
and never retains packet pointers.

## Consequences

- Project creation communicates the concrete component being generated.
- Template lineage is explicit and available to later migration tooling.
- Existing schema-v1 projects without provenance remain valid.
- Imported templates remain a later Phase 12 increment and cannot execute code
  merely because provenance is present.
