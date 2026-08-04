# ADR 0010: Versioned source-first snippets

- Status: Accepted
- Date: 2026-07-24

## Context

Foundry needs fast method insertion, defensive workflow patterns, progressive
learning, profile compatibility, and a future path for project, user, and
community content. Snippets must not obscure the C# that is ultimately built.

## Decision

Snippet catalogues use a versioned JSON contract. A definition declares its
provenance, exact target profiles, required CPH methods, security capabilities,
and a C# body containing numbered placeholders.

Expansion is UI-independent and returns ordinary text plus relative placeholder
spans. AvalonEdit converts those spans to document anchors only after insertion,
keeping the contract independent of the desktop control. The editor reserves
lowercase `cph.` for snippet prefixes while uppercase `CPH.` continues to mean
direct method completion.

Built-in compatibility is checked against the generated CPH catalogue. Default
expansions are compiled as C# 7.3 against profile-specific typed proxies.

Phase 5B adds optional guide metadata to schema v1. It labels placeholders,
declares their value kinds and choices, and drives a profile-filtered browser
with live preview. String content is escaped by the UI-independent expansion
service; identifiers, choices, Booleans, and integers are validated before
insertion. Configured values retain document-anchor spans for post-insertion
Tab navigation.

Catalogue revision 1.2.0 completes the initial inventory gate with 20 method
snippets and 10 defensive workflow snippets. Every entry retains exact profile,
required-method, guide, provenance, and security metadata.

## Consequences

- Snippet definitions are readable, diffable, and independently validatable.
- Inserted code has no dependency on the snippet engine.
- Placeholder positions remain stable as earlier placeholders are edited.
- Compatibility metadata cannot silently name an unknown profile or method.
- Guided forms, project-local sources, and community trust UI can be added
  without changing the expansion representation.
- Phase 5A does not yet provide a snippet browser or guided insertion form.
