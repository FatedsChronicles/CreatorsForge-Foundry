# ADR 0023: Source-first components and composed snippet libraries

## Status

Accepted for Phase 12B.

## Decision

Reusable components are copied into a project as readable source files. Their
stable ID, semantic version, and file inventory are recorded in `.foundryproj`,
and compiled files become explicit managed/native build inputs. Installation
must refuse file replacement and duplicate component IDs.

Snippet libraries are composed in a fixed order: verified built-ins, user
catalogues, then project catalogues. Every external file is independently
validated. Conflicting IDs or prefixes reject that complete catalogue; external
content cannot claim built-in provenance.

## Consequences

- Builds have no hidden Foundry component runtime dependency.
- Shared code remains editable, reviewable, and version-controlled.
- Catalogue failures are isolated and cannot silently alter built-in behavior.
- Component upgrades require an explicit future migration operation rather than
  overwriting user-edited source.
