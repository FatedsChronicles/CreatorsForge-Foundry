# ADR 0024: Active-project multi-project workspaces

## Status

Accepted for Phase 12C.

## Decision

A `.foundryworkspace` groups project-relative `.foundryproj` files and selects
one active project. Editor, designer, test, release, and deployment operations
retain their existing single-project semantics. Workspace build is the explicit
operation that iterates all members.

Every member must load successfully before the workspace opens. Paths must stay
beneath the workspace root and are stored with forward slashes for portability.

## Consequences

- Mixed Streamer.bot and OBS work can share one navigation surface.
- Existing project files and command behavior remain compatible.
- Deployment cannot accidentally fan out across the workspace.
- Cross-project references are not implicit; shared code remains an explicit
  component or build input.
