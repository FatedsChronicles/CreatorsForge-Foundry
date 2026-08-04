# Template interchange and project migration

Phase 12D completes the reusable-project workflow with reviewable
`.foundrytemplate` packages and an explicit schema migration operation.

## Importing and exporting templates

Use **File > Export Project Template** with an open project. Foundry saves the
project manifest blueprint and allowlisted text files (`.cs`, `.c`, `.h`, JSON,
Markdown, XML, YAML, and related build text). Build output, binaries, hidden
tool state, and unrecognised file types are excluded.

Use **File > Import Project Template** to select a package and provide a new
project name, reverse-DNS ID, compatibility profile, and empty destination
folder. Foundry parameterises the manifest and source namespace/module identity,
validates the result, and only then writes the new project. Imported projects
record the source template ID, version, filename, and description.

Safety limits are 256 files and 4 MiB of JSON/text. Absolute paths, parent
traversal, duplicate paths, unknown JSON properties, invalid project blueprints,
and non-text payloads are rejected.

CLI equivalents:

```text
foundry template export project.foundryproj output.foundrytemplate
foundry template import output.foundrytemplate NewProject --name "New Project" --id com.example.new-project --profile 1.0.4-stable
```

## Migrating legacy projects

Use **Tools > Migrate Legacy Project**. Foundry first inspects the manifest and
shows the exact changes and backup path. No project is silently migrated during
open, build, or validation.

The reviewed migration path is schema 0 (including manifests where
`schemaVersion` is absent) to schema 1. It adds the current schema marker,
explicit defaults, empty component inventory, and inferred built-in template
provenance. Existing unknown root fields are retained through JSON extension
data.

Before replacement, the original bytes are written to
`<project>.schema0.backup`. Foundry refuses to overwrite that backup if its
content differs. The migrated manifest must pass the current validator before
the backup or project is changed, and the final project write is atomic.

CLI migration previews by default:

```text
foundry migrate project.foundryproj
foundry migrate project.foundryproj --apply
```

Future schemas are never migrated backward, and unknown historical schema
versions are rejected until a reviewed migration path is added.
