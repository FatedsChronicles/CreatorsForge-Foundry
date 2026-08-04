# Multi-project workspaces

Phase 12C adds `.foundryworkspace`, a small versioned file that groups multiple
existing `.foundryproj` projects without changing their individual manifests.
A workspace may mix Streamer.bot and OBS Studio projects.

## Desktop workflow

- **File > New Workspace** selects existing projects and writes a workspace in
  their common parent directory.
- **File > Open Workspace** loads every member and rejects the workspace if any
  project is missing or invalid.
- **File > Add Project to Workspace** adds another project beneath the workspace
  directory.
- Double-click a project or one of its files in the project tree to make that
  project active. The filled dot identifies the active project.
- **Build > Build Workspace** validates and builds every member in declared
  order. The Build panel reports a passed/failed row per project.

Editing, testing, packaging, release, design, and deployment commands continue
to operate on the active project. This avoids accidentally deploying every
project when only a workspace was opened.

## File contract

Schema v1 records a display name, an ordered list of project-relative
`.foundryproj` paths, and an optional startup project. Absolute paths, duplicate
members, and parent traversal are rejected. Relative paths make a complete
workspace portable when its directory is moved or committed to source control.

See `samples/FoundrySamples.foundryworkspace` and
`schemas/workspaces/foundry-workspace-v1.schema.json`.

Automation can use `foundry validate-workspace <file.foundryworkspace>` or
`foundry build-workspace <file.foundryworkspace>`. The latter stops at the first
failed member and returns a failing process exit code.
