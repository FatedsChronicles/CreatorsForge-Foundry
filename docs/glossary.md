# Creators Forge Foundry glossary

**CPH**  
The Streamer.bot API object exposed to inline C# actions.

**CPHInline bridge**  
Generated inline C# that transfers Streamer.bot arguments and CPH access into a
managed extension entry point.

**CPH catalogue**  
A versioned local inventory of verified CPH method signatures, documentation,
and exact target-profile availability used by completion and diagnostics.

**Snippet catalogue**  
A versioned collection of source templates with completion prefixes,
placeholder markers, provenance, security declarations, and exact target
compatibility.

**Snippet placeholder**  
A numbered editable span selected after snippet insertion. Foundry navigates
these spans with Tab and Shift+Tab before moving to the final caret position.

**Foundry project**  
A source-first workspace described by a versioned `.foundryproj` manifest.

**Workspace state**  
Local, non-project data such as recent projects, shell layout, preferences, and
dirty-document recovery snapshots. It is kept outside source-controlled
projects under the current user's local application-data directory.

**Target provider**  
An adapter that contains platform-specific validation, build, and packaging
behaviour. Streamer.bot is the first target provider; OBS targets come later.

**Target profile**  
A declared compatibility contract for a platform version and release channel,
such as Streamer.bot 1.0.4 stable.

**Design view**  
The non-running visual editing surface for WinForms artifacts.

**Runtime view**  
An isolated interactive preview that executes built user code outside the main
editor process.

**Mock CPH runtime**  
A test double that supplies event arguments, records CPH calls, and supports
assertions without connecting to a live channel.

**Package intermediate representation**  
A platform-neutral, validated in-memory description that a version-specific
package adapter converts to an installable package.

**Generated artifact**  
Source, metadata, or binaries produced by Foundry. Generated source identifies
its ownership and overwrite policy in a header.

**Compatibility spike**  
A short, evidence-driven experiment used to prove runtime, bridge, dependency,
and package-format assumptions before product APIs depend on them.
