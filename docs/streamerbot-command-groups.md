# Phase 25G — Streamer.bot command groups

Phase 25G makes the text-based command groups documented by Streamer.bot a
source-controlled part of a Foundry project. Definition schema v6 adds the
optional `group` property to each command and migrates schemas v1-v5 without
inventing a group.

## Designer behavior

The **Commands** tab contains an editable **Group** selector. It suggests group
names already used by commands in the project while still accepting a new name.
An empty value means that the command is ungrouped. Duplicating an editable
command retains its group; preserved read-only commands cannot be duplicated.

Group suggestions are case-insensitive, sorted, and refreshed without forcing
a complete DataGrid refresh. This avoids the re-entrant UI behavior previously
fixed in the Actions tab.

## Import and export contract

- The verified payload property is `data.commands[].group`.
- A grouped command is represented by its group name as text.
- An ungrouped generated command emits JSON `null`.
- Imported source IDs, command ordering, and trigger relationships are not
  changed by grouping.
- Preserved v23/v24 packages patch only editable command fields. Read-only and
  opaque content remains unchanged.
- Foundry does not create a separate wire ID for a command group because the
  verified contract represents it as text.

## Manual acceptance

1. Open a disposable Streamer.bot project in Foundry and choose **Tools →
   Streamer.bot Designer → Commands**.
2. Set the first command's **Group** to `Creator Commands`.
3. Add a second command and open its **Group** selector. Confirm `Creator
   Commands` is offered and remains readable in Dark and Light themes.
4. Enter `Utility Commands` directly for the second command and save.
5. Reopen the Designer and confirm both group values persist.
6. Duplicate the first command and confirm its group is retained while its
   Foundry command ID is new.
7. Clear the duplicate's group, save, build twice, and confirm the two package
   artifacts are identical.
8. Use **Build → View Package...** and confirm grouped commands contain their
   group text while the ungrouped command has `group: null`.
9. Import the package into disposable Streamer.bot 1.0.4, 1.0.5-alpha.34,
   1.0.5-beta.1, 1.0.5-beta.6, and 1.0.7 instances. Confirm the commands appear
   in the expected groups and the ungrouped command remains ungrouped.
10. Export the result from a disposable host, import it into a new Foundry
    project, rename one command group, and re-export. Confirm group names,
    command IDs, ordering, trigger links, and opaque content survive the
    matching-format round trip.

Imported code and sub-actions remain inert throughout analysis, editing, and
packaging.

## Designer workflow

- **Ctrl+Shift+D** opens the Streamer.bot Designer from the Foundry workspace.
- **Ctrl+S** or **Save** writes the definition atomically and keeps the Designer
  open so creators can continue editing.
- **Close**, Escape, or the window close button dismisses the Designer manually.
- **Open C# source** is the intentional exception: it saves, closes the Designer,
  and returns to the main editor so the selected source can be opened.
