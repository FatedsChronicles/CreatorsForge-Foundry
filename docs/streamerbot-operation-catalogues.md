# Streamer.bot operation catalogues

Phase 25C moves Streamer.bot trigger and native sub-action discovery behind a
versioned catalogue rather than adding a separately coded form for every
operation. The built-in catalogue is embedded in the build layer so the
desktop designer and exporter validation use the same evidence.

Each operation records its stable Foundry ID, entity kind, model kind,
category, native Streamer.bot type, output mode, compatible profiles, typed
fields, known input/output arguments, and documentation provenance. The v1
field vocabulary supports text, multiline text, Boolean values, command
references, and variable expressions. Later catalogue revisions can extend
this vocabulary without changing existing project definitions.

## Verified v1 inventory

| Foundry operation | Streamer.bot type | Profiles | Output |
| --- | ---: | --- | --- |
| Command trigger | 401 | All five supported profiles | Native |
| Test trigger | 702 | All five supported profiles | Native |
| Set Argument | 123 | All five supported profiles | Native |

These mappings are present in retained payload-v23 and payload-v24 captures.
Imported operation types outside this inventory remain visible and read-only.
Foundry does not infer a native type number from an operation name.

## Designer workflow

Open **Build > Streamer.bot Designer**, select an action, and choose **Add from
catalogue...** beneath Triggers or Sub-actions. Search by name, category, or
description. The palette filters entries against the active compatibility
profile, shows arguments produced by triggers, and creates fields from the
catalogue definition.

The Validation tab and build both reject an editable operation if its mapping
is absent, incompatible with the selected profile, or carries a native type
that differs from the reviewed catalogue. This protects deterministic v23 and
matching-format v23/v24 export from catalogue or project tampering.
