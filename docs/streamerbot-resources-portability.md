# Phase 25E — Streamer.bot resources and portability

Phase 25E adds source-first resource requirements to Streamer.bot definition
schema v4. Resources describe installation-specific values without placing
credentials in the project or claiming undocumented Streamer.bot wire fields.

## Resource model

Each resource has a stable ID, friendly name, type, required state,
portability classification, optional description and suggested value, an
optional validation pattern, and zero or more entity bindings. A binding uses
`entity-type:entity-id:property`; multiple bindings are separated with
semicolons in the Designer.

Supported types cover OBS scenes, sources, filters, inputs and transitions;
Twitch rewards; platform accounts; files, folders and executables; URLs;
integration connections; and custom installation-specific values.

Portability is explicitly classified as fully portable, reconnectable by
name, requiring confirmation after import, or requiring manual configuration.
The build writes `build/streamerbot/portability-report.json` and records it as
a `streamerBotPortabilityReport` package-IR artifact. Suggested values are not
copied into this report.

## Safety and validation

- Credential-like suggested values are rejected without printing the value.
- Absolute local paths cannot be labelled fully portable and are highlighted
  for destination-system review.
- Invalid URLs, regular expressions, duplicate bindings, missing entities,
  unsupported types and unsupported portability values are rejected.
- Unused resources and values requiring destination configuration are
  warnings.
- Imported absolute Execute C# references become explicit local-file resource
  requirements while the existing safe-reference export gate remains intact.
- Resources are Foundry metadata in this increment. They are not injected into
  undocumented payload fields and preserved payload content is not changed.

## Manual acceptance

1. Open a disposable Streamer.bot project and choose **Code > Streamer.bot
   Designer**.
2. Open **Resources** and confirm the grid and guidance are readable in Dark,
   Light and System themes.
3. Add an OBS scene resource named `Starting scene`, mark it required, choose
   `reconnectByName`, enter `Starting Soon`, and bind it to
   `action:default:sceneName`.
4. Add a local-file resource with an absolute disposable path and choose
   `confirmAfterImport`. Confirm validation reports advisory `SBD2007` and
   `SBD2008` warnings. These warnings do not block saving or building.
5. Change that path resource to `portable`. Confirm `SBD1016` blocks saving;
   restore `confirmAfterImport`.
6. Add an integration resource named `API token` with a disposable secret-like
   value. Confirm `SBD1015` blocks saving and does not show the value, then
   clear the value.
7. Enter a missing entity binding and confirm `SBD1013` blocks saving. Replace
   it with an existing entity ID.
8. Save, close and reopen the Designer. Confirm all resource fields and
   bindings persist.
9. Build twice and confirm both builds succeed with identical package output.
10. Open **Build > View Package...** and inspect the portability-report artifact.
    Confirm classifications, counts and bindings are present and suggested
    values are absent.
11. Import a representative payload containing an absolute Execute C# compiler
    reference. Confirm Foundry creates a local-file resource requirement and
    retains the established absolute-reference export protection.
12. Resolve the compiler reference, build and re-import the resulting package
    into every Streamer.bot host claimed by the retained v23/v24 adapter.
