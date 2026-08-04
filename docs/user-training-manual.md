# Creators Forge Foundry user training manual

This manual teaches the complete desktop workflow for Creators Forge Foundry
v1. It is written for creators who want to build, test, package, publish, and
maintain Streamer.bot extensions or native OBS Studio plugins without having to
learn the repository internals first.

Foundry is source-first and local-first. Your project files remain ordinary
text and source files, generated output is reviewable, deployment changes are
previewed before they are applied, and nothing is uploaded automatically.

## 1. Know the supported targets

Foundry v1 supports:

- Streamer.bot 1.0.4 stable;
- Streamer.bot 1.0.5-alpha.34;
- Streamer.bot 1.0.5-beta.1;
- OBS Studio 32.1.2 on Windows x64.

The three Streamer.bot profiles control editor compatibility checks. Foundry
currently exports the verified stable-v23 import format for all three profiles.
OBS Studio 32.1.2 is the only exact OBS version supported for v1; a general
`32.x-windows-x64` label is not a promise of support for every 32.x release.

Use disposable Streamer.bot and OBS installations while learning, testing
deployment, or rehearsing repair, rollback, and uninstall. Do not begin these
exercises against a production streaming setup.

## 2. First launch and setup

On first launch, Foundry opens a setup screen. It checks Windows, the desktop
runtime, writable local storage, CMake, Visual Studio C++ tools, and the pinned
OBS SDK.

- Streamer.bot work needs the desktop/runtime checks to pass.
- OBS development also needs CMake, the Visual Studio C++ x64 toolchain, and
  the pinned OBS 32.1.2 SDK.
- Select **Development Toolchain...** to resolve OBS prerequisites immediately.
- Select **Finish Setup** when you have reviewed the results. Optional OBS tools
  may remain unavailable if you only intend to build Streamer.bot projects.

Foundry does not collect telemetry. Network access is disabled by default and
is used only when you enable it and explicitly request an SDK download or
update operation.

### Run setup checks again

Choose **Tools > Run Setup Checks...**, or press `F1`, to repeat the first-run
checks. Use this after installing or updating .NET, CMake, Visual Studio build
tools, or the OBS SDK.

### Configure the OBS development toolchain

Choose **Tools > Development Toolchain...**.

1. Read the current CMake, MSVC, and SDK status.
2. To work online, first enable explicit network access in Settings, then
   select **Install SDK**. Foundry downloads the official archives, checks their
   SHA-256 hashes, and creates the local development SDK.
3. To work offline, select **Use Offline Archives...** and choose a folder that
   contains both official archives. The same checksum verification applies.
4. Use **Copy SDK Path** when another local tool needs the verified SDK path.

The SDK is a development cache. It is not copied into your plugin package.

## 3. Tour of the main window

The main window has five working areas:

1. **Project** on the left lists the active project or every project in an open
   multi-project workspace. Double-click a file to open it. In a multi-project
   workspace, double-click a project or one of its files to make it active.
2. **Document tabs** in the centre hold open source and text files. An edited
   document is marked dirty until saved.
3. **Problems**, **Build**, and **Console** sit below the editor. Problems shows
   live editor, validation, build, and operational diagnostics. Build contains
   build output; Console contains other application activity.
4. **Inspector** on the right identifies the selected document, its project
   path, the current target, and the recovery policy.
5. The **status bar** reports the current operation and active target.

Drag the separators to resize the project tree, editor, inspector, and bottom
panel. Foundry remembers these dimensions. Choose **Tools > Reset Layout** to
restore the defaults.

The toolbar provides quick access to New, Open, Save, Format, Snippets, Build,
and Test. These buttons perform the same actions as their menu commands.

## 4. Create and open projects

### Create a project — File > New Project (`Ctrl+N`)

1. Enter a friendly **Project name**.
2. Enter a stable reverse-DNS **Project ID**, for example
   `com.example.chat-tools`. Treat this as a long-lived identity: it influences
   package names, deployment ownership, and deterministic Streamer.bot IDs.
3. Choose a **Parent folder**. Foundry creates the project beneath it.
4. Select **Streamer.bot extension** or **OBS Studio native plugin**.
5. Select the compatibility profile.
6. Select a project template.
7. Enter the author and a useful description.
8. Select **Create**.

Foundry generates the manifest and source-owned provider files, validates the
result, and opens it. It will not silently execute imported or generated user
code during project creation.

Streamer.bot templates:

- **Extension**: a minimal, test-triggered managed extension.
- **Command**: a command, queue, action, CPHInline bridge, and package-ready
  structured definition.

OBS templates:

- **Module**: an OBS module entry point without a source.
- **Passthrough filter**: a synchronous video filter that forwards rendering.
- **Configurable filter**: a filter with defaults and a Boolean property.
- **Video input**: a 1920×1080 synchronous input skeleton.
- **Encoded output**: an output service skeleton with paired capture lifetime.

### Open a project — File > Open Project (`Ctrl+O`)

Select a `.foundryproj` file. Foundry validates it before exposing the bounded
project tree. Generated folders, source-control data, reparse points, excessive
tree depth, and over-large documents are excluded or rejected for safety.

If the current project contains unsaved work, Foundry asks whether to save or
discard it before switching. Cancelling leaves the current workspace intact.

### Open a recent project — File > Recent Projects

Choose an entry to reopen its project or workspace. Hover an entry to see the
full path. The same unsaved-work prompt applies.

### Close a project — File > Close Project

Foundry asks about unsaved documents, then clears the current project or
workspace. Closing a project does not delete it or its build output.

### Exit — File > Exit

Foundry prompts for unsaved changes, writes the current layout and settings,
and closes. Cancelling the save prompt cancels exit.

## 5. Work with multiple projects

### Create a workspace — File > New Workspace...

1. Select one or more existing `.foundryproj` files in the project picker.
2. Choose where to save the `.foundryworkspace` file. Keeping it in the common
   parent directory makes its relative project paths portable.
3. Foundry creates the workspace and opens every member in declared order.

A workspace groups projects; it does not merge or rewrite them. It may contain
both Streamer.bot and OBS projects.

### Open a workspace — File > Open Workspace...

Select a `.foundryworkspace` file. Every referenced project must still exist
and validate. A missing or invalid member prevents the workspace from opening,
which avoids silently operating on an incomplete collection.

### Add a project — File > Add Project to Workspace...

This command is available only after opening a multi-project workspace. Select
an existing project located beneath the workspace directory. Foundry updates
the workspace file and reloads the member list.

### Choose the active project

Double-click a project node or any file belonging to it. The filled project
marker identifies the active project. Save, Test, Package, Designer, Publish,
and Deploy actions affect only this active project.

### Build every project — Build > Build Workspace

Foundry validates and builds each workspace member in declared order. The
Build panel reports a passed or failed row for every project and stops after a
failure. Use **Build Project** when you want to build only the active member.

## 6. Edit, save, and recover files

### Open a document

Double-click a supported file in the project tree. Foundry opens it in a tab.
C# files receive Roslyn-backed editing; C and header files receive pinned OBS
native editing assistance. Verified SDK headers opened by definition navigation
are read-only.

### Save — File > Save (`Ctrl+S`)

Saves the selected document. Foundry constrains writes to the project directory
and uses safe persistence rules.

### Save all — File > Save All (`Ctrl+Shift+S`)

Saves every dirty document in the current workspace. Build, Test Explorer, and
several designer operations also save or require saved input before continuing.

### Close a document — File > Close Document

Closes the selected tab. For a dirty document choose:

- **Yes** to save and close;
- **No** to discard the in-memory edit and close;
- **Cancel** to keep editing.

### Recovery snapshots

Foundry periodically copies dirty documents to local recovery storage. This is
crash recovery, not a substitute for saving or source control. If a newer
snapshot exists when a document is reopened, Foundry offers to restore it.
Review the recovered text and then save it normally.

Set the interval under **Tools > Settings > Workspace** from 10 to 600 seconds.

### Problems and diagnostic navigation

The Problems tab combines live compiler, compatibility, build, and application
diagnostics. Read the severity, code, line, and message together. Double-click
a diagnostic with a source location to open its file and place the caret at the
reported line. A successful build is the authoritative check even when the
live editor has no problems.

## 7. Use the C# and CPH editor to its fullest

Foundry analyses all C# files declared by the managed build, including unsaved
text in open tabs. The project contract uses C# 7.3 and .NET Framework 4.8.1,
matching the extension build rather than Foundry's own desktop runtime.

### Format a C# document — Code > Format Document (`Ctrl+Alt+F`)

Open and select a C# document, then run Format. Foundry applies Roslyn formatting
and leaves the document dirty so you can review and save the result. The command
does not apply to native C, headers, JSON, or read-only SDK files.

### Go to a source definition — `F12`

Place the caret on a symbol and press `F12`.

- For project C# symbols, Foundry navigates across declared source files.
- For known OBS symbols, Foundry opens the declaration in the verified pinned
  SDK header as a read-only tab.
- Navigation is unavailable when the symbol cannot be resolved or the pinned
  SDK integrity check is unhealthy.

### Use CPH completion and parameter help

1. Type uppercase `CPH.` to open method completion automatically.
2. Use `Ctrl+Space` to reopen completion after `CPH.`.
3. Select a method after reviewing its summary, overloads, profile availability,
   deprecation status, examples, and cautions.
4. Type `(` to open signature help; commas advance the active parameter.

Completion is filtered to the project's Streamer.bot profile. Problems reports
unknown, deprecated, and profile-incompatible calls.

### Browse the CPH reference — Code > CPH Method Reference...

This command requires an open Streamer.bot project. Search by method, category,
platform, or summary; select a method to inspect signatures, guidance, examples,
compatibility, and related methods. The catalogue is local and works offline.

### Insert a snippet by prefix

Type a lowercase prefix such as `cph.sendmessage` and choose the completion.
Lowercase `cph.` is the snippet convention; uppercase `CPH.` is method
completion. After insertion:

- press `Tab` to move to the next placeholder;
- press `Shift+Tab` to move to the previous placeholder;
- press `Escape` to leave placeholder mode.

Inserted snippets are ordinary editable C# and add no Foundry runtime dependency.

### Use the guided snippet browser — Code > Snippet Browser (`Ctrl+Shift+I`)

1. Open a writable C# document and place the caret where the code belongs.
2. Search by prefix, name, category, or description.
3. Filter by all, method, or workflow snippets.
4. Select an entry and review its provenance and file, network, or process
   capabilities.
5. Complete the guided fields. The preview updates as values change, and Insert
   remains disabled while a value is invalid.
6. Select **Insert**, then use `Tab` and `Shift+Tab` to review placeholders.

Select **Import catalogue...** to add a schema-v1 snippet catalogue to your
local library. Foundry validates it and rejects duplicate IDs or prefixes and
false built-in provenance. Project-specific catalogues placed in
`.foundry/snippets` are discovered automatically and travel with the project.

## 8. Use the native OBS editor

Native `.c` and `.h` files use C/C++ highlighting and the pinned OBS 32.1.2
catalogue.

1. Type `obs_` or `OBS_` to open completion, or press `Ctrl+Space` after the
   prefix.
2. Type `(` after a function to see signature and parameter help; use commas to
   move through parameters.
3. Press `F12` on a known catalogue symbol to open its verified SDK header.
4. Treat live catalogue notices as early guidance. **Build Project** remains
   authoritative for C syntax, types, complete headers, and linking.

Choose **Code > OBS Native API Reference...** in an OBS project to search by
symbol, category, header, or description. The details pane shows the declaration,
parameter information, compatibility, lifetime notes, owning header, and
reference route. This catalogue is local and deterministic.

## 9. Add reusable source components

Choose **Tools > Reusable Components...** with a project open.

1. Select a provider-compatible component.
2. Read its description and installation status.
3. Select **Add to project**.

Foundry collision-checks the destination, copies reviewed source files, adds
them to the correct build inputs, and records their ID, version, and inventory
in the manifest. It refuses duplicate installation and will not replace an
existing file. Installed components are ordinary editable source.

The built-in managed components provide typed argument conversion and a
thread-safe cooldown. Native components provide paired context ownership and
bounded OBS setting helpers.

## 10. Design a Streamer.bot package (Phase 6 workflow)

Choose **Build > Streamer.bot Designer...** in a Streamer.bot project that
declares a target definition. Foundry saves open work before loading the
structured JSON model.

### Set package metadata

Enter the **Author** and **Description**. Use a description that explains the
imported behavior so a recipient can review it before accepting the import.

### Create queues

Open **Queues** and select **Add queue**.

- **ID** is the stable project-owned logical ID used by action references.
- **Name** is the visible Streamer.bot queue name.
- **Blocking** controls the queue behavior represented in the export.

Create queues before assigning their IDs to actions. Select a row and use
**Remove queue** to delete it; remove or update action references first.

### Create commands

Open **Commands** and select **Add command**.

- **ID** is the stable logical ID used by triggers.
- **Name** is the friendly command name.
- **Commands (comma separated)** contains chat aliases such as
  `!hello, !hi`.
- **Enabled** controls whether the command is active after import.
- **Case sensitive** controls alias matching.
- **Global cooldown** and **User cooldown** are non-negative durations expected
  by the model.

Select a command and use **Remove command** to delete it. Update triggers that
refer to the deleted ID before saving.

### Create actions, triggers, and sub-actions

Open **Actions** and select **Add action**.

- **ID** is the stable logical action ID.
- **Name** is the visible action name.
- **Enabled** controls whether the action is enabled after import.
- **Queue ID** must match a queue ID, or be left as allowed by the project model.
- **Concurrent** and **Always run** map to the supported Streamer.bot action
  behavior.

Select the action before editing its lower grids. Triggers and sub-actions in
those grids belong to the selected action.

For a trigger, select **Add** under Triggers, give it a unique ID, and choose
`command` or `test`. A `command` trigger must contain the exact **Command ID**;
a `test` trigger is useful for explicit test invocation and does not need a
command reference. For a sub-action, select **Add** under Sub-actions, enter a
unique ID, and choose `setArgument` or `executeBridge`. Set Enabled and fill
the Variable, Value, and Auto type fields when the selected kind uses them.

Use the matching **Remove** button to delete the selected nested item. IDs are
logical identities, not display text: keep them stable after distribution so
unchanged builds continue to produce the same deterministic wire GUIDs.

### Save and validate the design

Select **Save**. Foundry validates the entire cross-reference graph before
atomically replacing the definition. Duplicate IDs, missing queue or command
references, negative cooldowns, and unsupported trigger or sub-action kinds
prevent saving. Fix the status message and try again. **Cancel** closes the
designer without changes.

### Build and inspect the Phase 6 output

1. Choose **Build > Build Project** (`Ctrl+B`).
2. Confirm there are no error diagnostics.
3. Choose **Build > Package Viewer...**.
4. Confirm the artifact inventory includes the managed DLL, CPHInline bridge,
   Streamer.bot import package, package report, and package IR as appropriate.
5. Select the Streamer.bot package to inspect its safely decoded JSON payload.
6. Select the package report to confirm the stable-v23 adapter, counts, payload
   hash, and successful round trip.
7. Select the bridge to inspect generated `CPHInline.cs`.

The exporter encodes the generated bridge, derives wire IDs deterministically,
and decodes its own result for a structural round-trip check. A failed check
stops the build instead of exposing an unverified import code.

## 11. Design an OBS plugin

Choose **Build > OBS Plugin Designer...** in an OBS project.

1. Review or edit **Module name**, **Display name**, **Author**, and
   **Description**.
2. Choose a generated component template: module starter, passthrough filter,
   configurable filter, or video input.
3. Choose one of the project's declared native source files.
4. Enter a stable lowercase **Component ID** and the user-visible **OBS name**.
5. Compare **Generated preview** with **Current source**. The selected template
   owns the complete target `.c` file; saving may replace hand-written code.
6. Check **Replace the selected source file with the generated preview** only
   after reviewing the complete difference.
7. Select **Save Design**.

Foundry validates the pinned API/SDK contract, writes the manifest and source,
refreshes the tree, updates an open source tab, and reruns native diagnostics.
Select **Cancel** to make no changes.

Generated templates are safe buildable starting points, not finished effects.
Custom rendering, settings state, graphics resources, and shutdown behavior
remain your responsibility.

## 12. Build and inspect a project

### Build Project — Build > Build Project (`Ctrl+B`)

Foundry saves the relevant work, validates the manifest and provider model, and
runs the provider build. Watch the status bar, then review both Problems and
Build output. A clean editor does not replace this build.

Streamer.bot builds create the managed DLL, verified CPHInline bridge, import
package when enabled, package report, and `build/package-ir.json`. OBS builds
use the pinned SDK and create the Windows x64 module plus package metadata.

### Package Viewer — Build > Package Viewer...

The viewer requires a successful build with `build/package-ir.json`.

- The top summary shows project, version, provider, profile, and artifact count.
- The table shows each artifact's kind, package-relative path, size, and SHA-256.
- Selecting a Streamer.bot package decodes its envelope and pretty-prints the
  payload without importing it.
- Selecting the package report or bridge shows its text.
- Binary artifacts show their type, size, and hash rather than raw bytes.

If an artifact is missing or outside the build directory, the viewer refuses to
display it.

### Build Release Package — Build > Build Release Package

Use this for a development hand-off. Foundry performs a fresh validated Release
build, verifies every artifact by size and hash, creates a provider-specific
README and build manifest, copies the verified payload, creates a ZIP under
`build/release`, and reads the archive back to verify it.

This proves package provenance and integrity; it does not replace real-host
runtime testing. Use **Publish Release** for a distribution-ready bundle.

## 13. Run tests and compatibility matrices

Choose **Build > Test Explorer...** or press `Ctrl+T`. Foundry saves all files,
refreshes the project, and opens the testing workspace.

### Run the active profile

Select **Run Tests**. Streamer.bot projects run through the mock runtime. For an
OBS project, first select one saved disposable OBS installation. Foundry builds
fresh before running tests.

### Run the compatibility matrix

Select **Run Matrix**. Streamer.bot tests run every profile declared by the test
definition. For OBS, select one or more disposable installations. Each
profile/runtime cell receives an independent result.

Use **Add OBS...** to register another local disposable runtime. This saves the
machine-specific path in user settings, not in the project.

### Read results

- Filter by text across case name, ID, profile, runtime, and diagnostics.
- Filter by outcome to focus on failures or errors.
- Select a result to inspect event arguments, expected and actual assertion
  values, logs, recorded CPH calls, return value, and duration.
- Review the lower diagnostics table for build, runner, matrix-cell, and case
  diagnostics.
- Double-click an actionable diagnostic, or select it and choose **Open
  Diagnostic**, to close Test Explorer and navigate to the source line.
- Select **Cancel** during a long run to request safe cancellation.

The Streamer.bot mock verifies the Foundry bridge contract but does not emulate
every host behavior. OBS native tests isolate module loading and source
create/destroy callbacks in a helper process, but they do not replace the final
OBS GUI test. Always complete real-host checks before distribution.

## 14. Prepare and publish a release

### Edit metadata — Build > Publishing Metadata...

Complete these fields:

- **Version**: enter a semantic version or use Patch, Minor, or Major to bump it.
- **Package name**: a portable distribution name.
- **Summary** and **Authors**: clear recipient-facing identity; separate authors
  with commas.
- **Licence file** and **Changelog file**: project-relative files. The licence
  must be non-empty and the changelog must name the exact release version.
- **Homepage**, **Repository**, and comma-separated **Tags** as applicable.
- **Dependency inventory**: one line per dependency in the displayed
  `kind | name | version | licence | source` format.

Code signing is optional and disabled by default. If enabled, explicitly enter
the Windows SDK signing tool, current-user certificate thumbprint, and optional
timestamp URL. Foundry never searches for or chooses a certificate for you.
Select **Save** to validate and atomically update metadata and version.

### Validate Publishing — Build > Validate Publishing

This performs a provider build and reports required and recommended checklist
items. Fix every error, including missing legal files, a changelog/version
mismatch, invalid dependency data, provider build failures, or incomplete
signing configuration.

### Publish Release — Build > Publish Release

Publishing repeats the gates and creates the verified provider archive,
installation guidance, package IR, licence, changelog, publishing checklist,
dependency inventory, build manifest, signing evidence when enabled, and a
sibling reproducibility report containing the finished archive hash.

Publishing does not upload the archive. Review it and choose the distribution
channel yourself. Any signing or verification failure aborts publication.

## 15. Deploy and maintain installations safely

Choose **Build > Deploy / Manage Installation...**. Foundry opens the correct
provider dialog for the active project. Deployment is receipt-backed: Foundry
tracks only files it installed and protects files that have since changed.

### Standard deployment sequence

1. Choose a saved installation or select **Browse...** and locate a disposable
   host root.
2. Select **Check Health** to inspect installed/project versions, receipt
   validity, missing or modified files, and package drift.
3. Select **Preview Install / Update**. Depending on health, the button may read
   **Preview Update** or **Preview Repair / Redeploy**.
4. Read the summary, every change row, destination, size, hash, details, backup
   behavior, and recovery operation.
5. Check the review confirmation only after inspecting the plan.
6. Select **Apply Plan** and confirm the operation.
7. Perform the required checks in the real host, close it, and return to
   **Check Health**.

Never treat Preview as a formality. It is the point at which you verify that the
selected disposable host and every destination are correct.

### Streamer.bot-specific deployment

Close Streamer.bot before previewing or applying any mutation. Foundry blocks
deployment while the selected `Streamer.bot.exe` is running.

If the project does not yet request package output, the dialog offers **Enable
Package Output**. Review the prompt: Foundry adds `streamerBotPackage` and may
create a starter structured definition, after which you should inspect and
build the project.

After deployment:

1. Select **Copy Import Code** and paste it into Streamer.bot's import flow.
2. Add the deployed extension DLL as the required compiler reference.
3. Compile the imported Execute C# action.
4. Trigger the action and verify its expected runtime behavior or log.
5. Record **Package imported**, **DLL reference added**, **Code compiled**, and
   **Runtime verified**, then select **Save Checklist**.

The import code creates user-owned Streamer.bot configuration. Foundry uninstall
does not remove imported actions, commands, queues, or triggers; remove them
manually from the disposable host when finished.

### OBS-specific deployment

OBS must be closed before preview or apply. Foundry blocks install, update,
repair, rollback, and uninstall while the selected OBS process is running.

After applying an install or update:

1. Launch OBS and confirm there is no module-load error.
2. Add the plugin source or filter to a disposable scene.
3. Exercise it, save, restart OBS, and confirm attachment/settings persistence.
4. Remove the source/filter and close OBS cleanly.
5. Return to Foundry and select **Check Health**. Review the newest post-install
   OBS log status as well as versions, receipt, files, and drift.

### Repair, rollback, and uninstall

- **Repair / Redeploy** restores missing Foundry-owned files after you review a
  new plan. Modified files are called out and protected from silent overwrite.
- **Preview Rollback** shows how the previous recoverable deployment will be
  restored. Review and apply it like any other plan.
- **Preview Uninstall** shows every receipted file to be removed. Foundry refuses
  to remove files whose content no longer matches its ownership record.

These actions affect Foundry-owned deployment files and receipts only. They do
not remove user-owned Streamer.bot configuration, OBS scenes, or unrelated host
files.

## 16. Import, export, and migrate projects

### Export a template — File > Export Project Template...

Open the source project, save all work, choose a destination, and export a
`.foundrytemplate`. Foundry includes the manifest blueprint and allowlisted text
files. It excludes build output, binaries, hidden tool state, and unknown file
types. Review the package before sharing it.

### Import a template — File > Import Project Template...

1. Select a `.foundrytemplate`.
2. Enter a new project name and reverse-DNS ID.
3. Choose the target profile and a new empty destination folder.
4. Select **Import**.

Foundry validates paths, limits, schema, text payloads, and the parameterized
project before writing it, then opens the result. Imported source is not granted
special trust or execution permission.

### Migrate a legacy project — Tools > Migrate Legacy Project...

Select a schema-0 project. Foundry first inspects it and shows the exact planned
changes and backup path. If migration is required, confirm the plan. The original
bytes are written beside the project as a `.schema0.backup`, the new manifest is
validated, and replacement is atomic. Foundry will not overwrite a conflicting
backup or silently migrate during open/build.

## 17. Settings, updates, privacy, and diagnostics

### Settings — Tools > Settings (`Ctrl+,`)

**Workspace**:

- Set the default project folder used by project pickers.
- Set recovery autosave from 10 to 600 seconds.

**Updates**:

- Enter a local file path or HTTPS update-manifest location.
- Enable **Allow explicit HTTPS update checks and SDK downloads** only when you
  want Foundry to use the network for an action you start.
- Leave the location empty to disable update checks. Foundry never checks
  automatically.

**Privacy**:

- Read the local-data boundary.
- Enable local path inclusion in diagnostic bundles only when those paths are
  genuinely needed. Leave it disabled for a bundle you may share publicly.

Select **Save** to apply valid settings or **Cancel** to discard dialog edits.

### Check for updates — Help > Check for Updates...

1. Configure a local or permitted HTTPS manifest in Settings.
2. Select **Check for Updates**.
3. Review the current and available version and verification information.
4. If an update is available, select **Stage Verified Update**. Foundry checks
   the package size and SHA-256 before making it available.

Updates are never checked or installed silently. Preserve and verify projects,
settings, and recovery state as part of a release acceptance update rehearsal.

### Recovery and diagnostics — Tools > Recovery and Diagnostics...

The dialog lists local recovery snapshots and local failure reports. Nothing in
the list is uploaded.

Select **Create Diagnostic Bundle...**, choose a ZIP destination, and wait for
confirmation. Open the ZIP and review every file before sharing it. Bundles
include an issue-report template and local diagnostic evidence; they do not
include project source or recovery text. Local paths are redacted from the
system summary unless you explicitly enabled path inclusion.

### Privacy, offline use, and About

- **Help > Privacy and Offline Use** summarizes the local/offline boundary and
  the explicit network rule.
- **Help > About** shows product and version information. Record this exact
  version in issue reports and acceptance evidence.

## 18. Keyboard quick reference

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | Create a project |
| `Ctrl+O` | Open a project |
| `Ctrl+S` | Save the selected document |
| `Ctrl+Shift+S` | Save all documents |
| `Ctrl+B` | Build the active project |
| `Ctrl+T` | Open Test Explorer |
| `Ctrl+Alt+F` | Format the active C# document |
| `Ctrl+Shift+I` | Open the snippet browser |
| `Ctrl+Space` | Request context-appropriate completion |
| `F12` | Go to definition or a verified OBS header |
| `Ctrl+,` | Open Settings |
| `F1` | Run setup checks |
| `Tab` / `Shift+Tab` | Move through inserted snippet placeholders |
| `Escape` | Leave snippet placeholder mode or cancel a dialog |

Menu access keys are also available through `Alt` and the underlined menu
letters. Foundry uses Windows high-contrast colours when high contrast is active.

## 19. Recommended end-to-end training journeys

### Streamer.bot creator journey

1. Create a project from the Streamer.bot command template.
2. Open the generated C# and exercise CPH completion, signature help, snippets,
   formatting, and diagnostic navigation.
3. Open the Streamer.bot Designer and review the queue, command, action,
   trigger, and sub-action graph.
4. Build and inspect every artifact in Package Viewer.
5. Run Tests, then Run Matrix. Inspect assertions, logs, and CPH calls.
6. Complete publishing metadata, Validate Publishing, and Publish Release.
7. Deploy to each disposable supported host separately. Import, reference,
   compile, execute, and save the completion checklist.
8. Rehearse health detection, repair, update, rollback, and uninstall.

### OBS creator journey

1. Complete the development toolchain checks.
2. Create a project from the passthrough or configurable filter template.
3. Exercise native completion, parameter help, API Reference, and `F12` header
   navigation.
4. Open the OBS Plugin Designer and compare current source with a generated
   preview without overwriting work until you have reviewed it.
5. Build and inspect the module/package inventory.
6. Run Tests and Run Matrix against disposable OBS 32.1.2 installations.
7. Complete metadata, Validate Publishing, and Publish Release.
8. With OBS closed, deploy to a disposable installation. Run the real GUI
   lifecycle check, close OBS, then inspect health and the newest log.
9. Rehearse repair, update, rollback, modified-file protection, and uninstall.

## 20. What “ready to distribute” means

A project is not ready merely because it builds. Before sharing a release:

- resolve all build and publishing errors;
- inspect package contents, sizes, and hashes;
- pass project tests and the declared compatibility matrix;
- complete real-host testing on every host you claim to support;
- verify deployment health and provider-specific completion evidence;
- include and review the licence, changelog, dependencies, build manifest,
  publishing checklist, and reproducibility report;
- test install/update/repair/rollback/uninstall on disposable hosts;
- review the exact final archive you intend to distribute.

Foundry supplies the evidence and safety rails. The creator remains responsible
for source behavior, host testing, licensing, and the final release decision.
