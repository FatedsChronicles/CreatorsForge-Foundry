# Streamer.bot Phase 6 export contract

## Captured fixtures

Representative populated exports were captured on 2026-07-25 from:

- 1.0.4 stable;
- 1.0.5-alpha.34;
- 1.0.5-beta.1.

Each contains one action, one non-blocking queue, one command with two aliases,
5/10-second global/user cooldowns, a command trigger, a Test trigger, a Set
Argument sub-action, and an Execute C# Code sub-action.

## Confirmed common contracts

| Item | Meaningful wire fields |
| --- | --- |
| Queue | `id`, `name`, `blocking` |
| Action | `id`, `queue`, `name`, `enabled`, history/pending flags, group, execution flags, `triggers`, `subActions`, `collapsedGroups` |
| Command trigger | `id`, `type: 401`, `commandId`, `enabled`, `exclusions` |
| Test trigger | `id`, `type: 702`, `variables`, `enabled`, `exclusions` |
| Set Argument | `id`, `type: 123`, `variableName`, `value`, `autoType`, execution fields |
| Execute C# | `id`, `type: 99999`, `byteCode`, `references`, compile/result fields, execution fields |

The Execute C# `byteCode` property is Base64-encoded UTF-8 C# source. It is not
an opaque compiled assembly.

Commands contain the user-visible name, newline-separated command/aliases,
enabled/include/mode/location flags, bot/internal behavior, sources,
counter-persistence flags, case sensitivity, cooldowns, group, and grant type.
Stable additionally names `permittedUsers`, `permittedGroups`,
`regexExplicitCapture`, and `ignoreInternal` directly.

## Version differences

| Profile | Payload | Command representation |
| --- | ---: | --- |
| 1.0.4 stable | 23 | Stable readable property names |
| 1.0.5 alpha | 24 | Several command properties serialized under build-specific obfuscated names |
| 1.0.5 beta | 24 | Same semantics, different obfuscated names from alpha |

The prerelease property names cannot be treated as a durable public contract.
The first Foundry exporter therefore targets 1.0.4 stable payload v23. Alpha
and beta remain readable/import-compatible profiles, but writing their native
v24 command objects requires either an official stable contract or
version-specific evidence for every supported build.

## Adapter requirements

- Generate deterministic IDs from project-owned stable IDs.
- Emit stable payload v23 with `exportedFrom: "1.0.4"`.
- Base64-encode generated CPHInline source into `byteCode`.
- Emit no machine-specific compiler reference paths.
- Preserve queue, action, command, and trigger references.
- Decode the generated envelope and compare the meaningful model.
- Reject unsupported item kinds rather than dropping them.
- Report v24 native export as unsupported until its command contract is stable.
