# Streamer.bot import-to-project

Use **File > Import > Streamer.bot Import Code...** to paste another
developer's Streamer.bot export or load it from a file. Select **Analyze
safely** before choosing a new destination folder. Analysis only decodes data;
it never loads or runs C#, DLLs, scripts, triggers, or sub-actions.

Foundry currently creates projects from verified payload-v23 and payload-v24
packages. Other payload versions display their inventory but remain
analysis-only until representative exports have been verified. Credential-like
properties block creation and diagnostics show only their JSON location, never
their value.

The preview distinguishes supported editable entities from unsupported
read-only entities. Foundry preserves unsupported triggers, sub-actions,
timers, WebSocket definitions, unknown properties, ordering, and source GUID
relationships in a dedicated sidecar. The clean editable definition remains
`streamerbot/streamerbot.json`.

Execute C# bodies are extracted beneath `streamerbot/code/<action>/<subaction>.cs`
and open in the normal Foundry editor. They are not managed-library build
inputs. A matching adapter re-embeds edits during build without executing the
source. Absolute compiler paths and machine paths must be resolved before
export. For an imported Execute C# node, edit its `references` array in
`streamerbot/streamerbot.json`: remove machine-specific framework paths and
retain or replace required dependencies with portable project-relative entries.
The designer also provides **Remove absolute references** for the selected
editable Execute C# sub-action; review the remaining relative entries before
saving and building.

Imported projects intentionally contain no generated DLL, CPHInline bridge,
automatic licence, or invented tests. Review the author's terms, add an
authorised licence, document attribution, and create tests before publishing.
Same-format export preserves the original payload version and `exportedFrom`;
Foundry does not transform opaque content across payload versions.
