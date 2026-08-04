# OBS plugin designer and templates

Phase 9C adds a structured designer for the first SDK-backed OBS component in a
Foundry project. Open it with **Build > OBS Plugin Designer**.

## Designer workflow

The designer edits reviewable module metadata and a versioned `obsPlugin.design`
block. It shows the current C source beside a deterministic generated preview.
Saving a changed template requires an explicit source-replacement confirmation;
closing or cancelling the dialog makes no changes.

The form validates module metadata, the selected declared native source, the
stable lowercase component ID, the OBS-visible component name, and the pinned
`libobs-module-v1` API/SDK contract.

After a successful save, Foundry rewrites the selected source and manifest,
refreshes the workspace tree, updates an open source tab, and reruns native
catalogue diagnostics.

## Built-in templates

| Template ID | Designer name | Generated behavior |
| --- | --- | --- |
| `module-starter-v1` | Module starter | Loads and logs without registering a source. |
| `passthrough-filter-v1` | Passthrough video filter | Registers an Effect Filter and forwards rendering unchanged. |
| `configurable-filter-v1` | Configurable video filter | Adds defaults and a Boolean property to the passthrough structure. |
| `video-input-v1` | Video input source | Registers a 1920x1080 synchronous input skeleton with transparent rendering. |

Each template owns the complete selected `.c` file, includes
`<obs-module.h>`, implements the configured Foundry entry symbol, creates
portable C identifiers from the component ID, and escapes user-visible strings.
Generated files carry the exact template revision in their first comment.

Templates provide small, buildable extension points. Custom rendering,
settings state, graphics resources, and shutdown behavior remain
developer-owned code after generation.

## Project representation

```json
{
  "obsPlugin": {
    "design": {
      "template": "passthrough-filter-v1",
      "source": "src/plugin.c",
      "componentId": "dev.creator.my-filter",
      "componentName": "My Filter"
    }
  }
}
```

Projects created before Phase 9C remain valid. Opening the designer derives a
safe initial design from existing OBS metadata, but it never replaces legacy
or hand-authored source without confirmation.

## Verification

Unit tests cover deterministic generation, C string escaping, invalid designs,
schema diagnostics, project creation, and safe persistence. The native desktop
smoke test materializes the designer against the passthrough sample. A pinned
SDK verification builds every template through the real CMake/MSVC pipeline.
