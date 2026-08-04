# Project templates and guided creation

Phase 12A replaces the provider-only new-project choice with a versioned
template catalogue and guided parameters. Every generated `.foundryproj`
records its template ID, revision, author, and description so later migrations
can distinguish template lineage from user-authored settings.

## Built-in catalogue

Streamer.bot:

- `streamerbot-extension-v1` — minimal test-triggered extension;
- `streamerbot-command-v1` — command, queue, action, and CPHInline bridge.

OBS Studio:

- `obs-module-v1` — module entry point;
- `obs-passthrough-filter-v1` — synchronous video filter;
- `obs-configurable-filter-v1` — filter with defaults and properties;
- `obs-video-input-v1` — video input source;
- `obs-output-v1` — encoded output service.

All OBS component templates use explicit create/destroy ownership. The output
template also pairs start/end data capture and exposes the encoded-packet
callback without retaining packet pointers.

## Guided creation

Choose **File > New Project**, select the provider and compatibility profile,
then select a template. The form captures project identity, author, and
description. Foundry generates the manifest and source-owned provider files,
validates them, and opens the resulting project.

The manifest stores provenance as:

```json
"template": {
  "id": "obs-output-v1",
  "revision": "1.0.0",
  "parameters": {
    "author": "Creator",
    "description": "Encoded output integration"
  }
}
```

Template provenance is informational build input in schema v1; it never grants
permission to execute imported code.
