# Phase 25F manual acceptance

Use the latest Release desktop build and disposable Streamer.bot hosts. The
implementation never executes C# inside Foundry.

## Manual Execute C#

1. Create a **Streamer.bot C# action package**, or open an existing
   source-first project, then open **Tools > Streamer.bot Designer**. The C#
   action-package template deliberately contains no managed bridge.
2. Select an editable action and choose **Add Execute C#...**.
3. Confirm the row reports `executeCSharp`, a confined
   `streamerbot/code/<action-id>/<subaction-id>.cs` path, and `Manual` state.
4. Choose **Open C# source**. Confirm the definition is saved and the file
   opens in the Roslyn editor with `using System;`, an editable
   `CPHInline.Execute()` scaffold, and the `// your main code goes here` marker.
5. Edit and save the source. Reopen the Designer and confirm it remains manual.
6. Confirm the `.cs` file is not listed as a managed DLL build input.

## Verified Set Argument conversion

1. Add a Set Argument sub-action with **Auto type cleared**. Include quotes,
   backslashes, and a line break in its value.
2. Select it and choose **Convert to C#...**.
3. Review the complete source preview and confirm it contains an escaped
   `CPH.SetArgument(...)` call and `using System;`. Cancel once and confirm no source file or model
   change remains.
4. Repeat and choose **Convert**. Confirm the same sub-action ID, position,
   enabled state, and weight are retained; its kind becomes `executeCSharp`
   and its C# state is `Generated`.
5. Save, reopen, and confirm the state is still `Generated`.
6. Edit the source in the Roslyn editor, reopen the Designer, and confirm the
   state changes to `Detached`. Save again and confirm the edit is unchanged.
7. Attempt conversion with **Auto type selected** and confirm Foundry blocks it
   with an explanation instead of approximating native coercion.

## Build and round trip

1. Build twice without changing inputs. Confirm the Streamer.bot import code
   and package report hashes are identical.
2. Inspect **Build > View Package...** and confirm Execute C# is embedded in the
   Streamer.bot package, while no extra managed compilation input is present.
3. For a source-authored project, import the result into each compatible host
   claimed by the project and confirm the Execute C# block compiles.
4. Import a representative v23 or v24 project, convert an editable Set
   Argument, build, and import the same-format result. Confirm unsupported
   preserved nodes and source IDs remain intact.
5. Run the converted action in disposable hosts and confirm the argument value
   is set exactly, including escaped characters.

Acceptance should cover Streamer.bot 1.0.4, 1.0.5-alpha.34,
1.0.5-beta.1, 1.0.5-beta.6, and 1.0.7 wherever the selected adapter/profile
claims compatibility.

Existing managed-template projects may still contain an `executeBridge`
sub-action. Keep it only when the action intentionally calls the generated
Foundry DLL. For a Designer-authored C#-only action, select that bridge row and
choose **Remove** before building the import package.
