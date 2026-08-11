# Streamer.bot importer experience

Phase 25D makes third-party import files easier to open without weakening the
Phase 25A safety boundary.

## Files and drag-and-drop

**Load from file** lists `.txt`, `.sb`, and `.streamerbot` exports directly and
also accepts developer-defined extensions. Foundry identifies the export from
its bounded SBAE contents, never from its filename suffix. The same reader is
used when one file is dropped onto the import-code area.

The reader accepts one local regular file, enforces the 16 MiB import-code
limit, and decodes strict UTF-8. Folders, shortcuts, multiple files, URLs,
invalid text, and oversized files are rejected before package analysis. Paste
remains available and uses the existing envelope limits and credential scan.

## Creation suggestions

Project Name derives a package-ID slug and folder name. For example, `Bot
Eliminator` suggests `com.example.bot-eliminator` and `BotEliminator` beneath
the configured project directory. Each derived field stops tracking as soon as
the user edits it manually. The adjacent **Suggest** control restores tracking;
choosing a parent with **Browse** keeps folder-name tracking within that parent.

Re-analyzing the import does not overwrite creation fields after the first
successful preview.

## Friendly source labels

Imported Execute C# files retain confined stable paths such as
`streamerbot/code/<action-id>/<sub-action-id>.cs`. Solution Explorer displays
the action name for the containing folder and an ordered `Execute C# Code`
label for each file. Tooltips and copy-path commands expose the real path.

These aliases never rename files. Definition-owned C# sources and their
containing directories are protected from Solution Explorer rename, move, and
delete actions so visual naming cannot break deterministic re-export.
