# Snippets and learning foundation

Phase 5A introduces a source-first, profile-aware snippet system. Snippet
catalogues are ordinary JSON validated against
`schemas/snippets/snippet-catalogue-v1.schema.json`. Inserted output is ordinary
editable C# and does not retain a proprietary runtime dependency.

## Using built-in snippets

Type a lowercase snippet prefix in a C# document. Completion opens after a
prefix separator such as the period in `cph.`. `Ctrl+Space` can also request
completion.

Use **Code > Snippet Browser**, the **Snippets** toolbar button, or
`Ctrl+Shift+I` for searchable guided insertion. The browser only lists snippets
compatible with the open project's target profile. Search covers prefixes,
names, categories, and descriptions; the kind filter separates method and
workflow snippets.

The verified library contains 20 method snippets:

`cph.sendmessage`, `cph.global.get`, `cph.global.set`,
`cph.global.unset`, `cph.argument.set`, `cph.action.exists`,
`cph.action.run.id`, `cph.log.debug`, `cph.log.info`, `cph.log.warn`,
`cph.log.error`, `cph.twitch.whisper`, `cph.twitch.reply`,
`cph.youtube.message`, `cph.obs.scene`, `cph.obs.scene.current`,
`cph.obs.source.visibility`, `cph.obs.source.mute`, `cph.obs.text.set`, and
`cph.wait`.

The 10 workflow snippets are:

`cph.trygetarg`, `cph.action.run`, `cph.workflow.counter`,
`cph.workflow.twitch.timeout`, `cph.workflow.redemption.fulfill`,
`cph.workflow.obs.scene`, `cph.workflow.twitch.whisper`,
`cph.workflow.command.cooldown`, `cph.workflow.action.run`, and
`cph.workflow.youtube.message`.

After insertion, the first placeholder is selected. Use `Tab` and `Shift+Tab`
to move forward and backward. The `$0` marker represents the final caret
position and is removed from inserted source. Press `Escape` to leave the
placeholder session.

Uppercase `CPH.` remains reserved for method completion from the CPH catalogue.
Lowercase `cph.` is the built-in snippet prefix convention.

## Guided insertion

Selecting a browser entry displays its description, provenance, declared
security capabilities, configuration fields, and a live code preview. Field
metadata distinguishes:

- C# string content, which is escaped before insertion;
- identifiers, which must be valid C# identifiers;
- Boolean and enumerated choices;
- integers;
- types and arbitrary C# expressions.

Insert remains disabled while a value is invalid. Configured values retain
their placeholder spans after insertion, so they can still be reviewed and
replaced with `Tab` and `Shift+Tab`.

## Catalogue contract

Catalogue schema version 1 records:

- stable snippet identity, semantic version, author, and provenance;
- target, language, and method or workflow kind;
- one or more completion prefixes;
- exact compatible Streamer.bot profile identifiers;
- categories and required CPH methods;
- an ordered C# body;
- file, network, and process-execution security declarations.

Guide metadata is additive and optional in schema v1, allowing existing
catalogues to fall back to generic C# value fields. Built-in definitions live in
`src/CreatorsForge.Foundry.Editor/Snippets/streamerbot-builtins-v1.json` and are
embedded into the editor assembly. The current catalogue revision is `1.2.0`.

## Placeholder syntax

`${1:defaultText}` declares a numbered editable placeholder. Positive indices
are visited in numerical order. `$0` declares the optional final caret
position. Multi-line expansion preserves the indentation of the line where
the prefix was inserted.

Malformed markers are rejected with `CFS5004`. Catalogue loading also reports:

- `CFS5001`: malformed JSON or unsupported schema;
- `CFS5002`: duplicate snippet identity or prefix;
- `CFS5003`: invalid metadata, profile, or required CPH method.

## Compatibility and quality gate

The loader verifies every declared profile against the generated CPH
catalogue. Each required method must exist and expose an overload for every
declared profile.

Automated tests expand the guided default form of every built-in snippet and compile
the result as C# 7.3 against a strongly typed proxy generated from the selected
profile's catalogue signatures. This verifies syntax, argument lists, generic
calls, and return-value usage for stable, alpha, and beta.

The Phase 5 inventory gate of at least twenty method snippets and ten workflow
snippets is satisfied.

## User and project catalogues

Phase 12B extends the browser with **Import catalogue...**. User catalogues are
validated and copied into Foundry's local snippet library. Project-owned
catalogues placed in `.foundry/snippets` are loaded automatically. IDs and
prefixes must remain unique across the combined library; an external catalogue
cannot replace a built-in definition or claim `built-in` provenance. See
`samples/snippets/user-catalogue-v1.json` for an editable example.
