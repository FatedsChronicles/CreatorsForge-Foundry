# Reusable components

Phase 12B adds a source-first component library for sharing small pieces of
managed and native implementation without adding a Foundry runtime dependency.

Open a project and choose **Tools > Reusable Components**. Foundry only shows
components for the active provider. Adding one performs a collision check,
copies its reviewed source files into the project, adds compiled files to the
managed or native build inputs, and records its ID, version, and complete source
inventory in `.foundryproj`.

The initial library contains:

- `creatorsforge.managed.arguments` — typed Streamer.bot argument conversion;
- `creatorsforge.managed.cooldown` — a thread-safe, testable cooldown gate;
- `creatorsforge.native.owned-context` — paired OBS context allocation/freeing;
- `creatorsforge.native.settings` — bounded and non-empty OBS setting helpers.

Installed files are ordinary editable C# or C source. Foundry refuses to
replace an existing file and refuses duplicate component installation. A build
therefore remains transparent and deterministic after a component is added.

## User snippet catalogues

The snippet browser can import any valid schema-v1 JSON catalogue. Imported
files are stored under the user's local Foundry `snippets` directory. A project
can also carry catalogues in `.foundry/snippets`; those travel with the project.

Foundry validates every catalogue against the selected CPH compatibility data.
External catalogues cannot claim built-in provenance. Duplicate IDs or prefixes
are rejected instead of silently overriding another snippet. Compatible user
and project snippets participate in browser search, guided insertion, and
prefix completion alongside the verified built-ins.

Use `samples/snippets/user-catalogue-v1.json` as a starting point.
