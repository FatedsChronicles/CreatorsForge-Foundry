# Streamer.bot designer and stable-v23 exporter

Phase 6 turns the source-first package model into a Streamer.bot import code
without loading a user extension into the Foundry process.

## Structured definition

A project requesting `streamerBotPackage` declares:

```json
{
  "targetDefinition": "streamerbot/streamerbot.json",
  "outputs": [
    "managedLibrary",
    "cphInlineBridge",
    "streamerBotPackage"
  ]
}
```

The definition schema is
[`schemas/streamerbot/streamerbot-definition-v1.schema.json`](../schemas/streamerbot/streamerbot-definition-v1.schema.json).
It models metadata, action queues, commands and aliases, actions, triggers, and
supported sub-actions. Logical IDs are project-owned readable strings.
Validation rejects duplicate IDs, missing queue/command references, negative
cooldowns, and unsupported trigger or sub-action kinds.

Use **Build > Streamer.bot Designer** in the desktop to edit this model with
structured tables. Actions expose their triggers and sub-actions in nested
grids. Saving validates the complete cross-reference graph before replacing
the JSON file.

## Stable-v23 adapter

The current writer emits the proven Streamer.bot 1.0.4 wire contract:

- payload `version: 23`;
- `exportedFrom: "1.0.4"`;
- `SBAE` signature followed by a GZip-compressed UTF-8 JSON payload;
- the resulting envelope represented as Base64 import text.

Every wire GUID is deterministically derived from the project ID, item kind,
and logical ID. Queue, command, trigger, and action references therefore remain
stable across unchanged builds while separate item namespaces cannot collide.

The generated `CPHInline.cs` source is UTF-8/Base64 encoded into the Execute C#
`byteCode` field. The writer emits an empty reference list, so exports contain
no developer-machine framework paths.

Before completing a build, the adapter decodes its own generated envelope and
compares the JSON model structurally. Failure stops the build instead of
publishing an unverified import code.

## Build outputs and viewer

```text
build/
├── bridge/CPHInline.cs
├── managed/<assemblyName>.dll
├── streamerbot/<projectId>.streamerbot
├── streamerbot/package-report.json
└── package-ir.json
```

The report records the adapter, payload/export versions, model counts, payload
SHA-256, and round-trip result. Both files are hashed in the package IR.

Use **Build > Package Viewer** after a build to inspect the artifact inventory.
Selecting a Streamer.bot package safely decodes and pretty-prints its payload;
selecting the report or bridge displays its text.

The writer always emits the stable-v23 contract. Projects may retain any of the
three verified stable, alpha, or beta compatibility profiles for editor
diagnostics. The captured compatibility matrix confirmed that stable-v23
exports import into all three supplied installations. Native v24 writing
remains disabled because prerelease command property names differ between the
captured alpha and beta builds.
