# Solution Explorer

Foundry's Solution Explorer displays the active project or multi-project
workspace as a hierarchical, type-labelled tree. Double-click an editable file
to open it. Use **Add** in the pane header or **Add New Item…** from the
right-click menu to create an item in the selected folder. When a file is
selected, its containing folder is used.

The new-item dialog supports:

- C# (`.cs`)
- C++ (`.cpp`)
- C (`.c`)
- C/C++ headers (`.h`)
- JSON, XML, HTML, CSS, JavaScript, and TypeScript
- Markdown and text documents
- `CMakeLists.txt`
- folders

If a file extension is omitted, Foundry supplies the type's default extension.
New text/code files open immediately after creation. **Refresh** rescans the
active project while keeping current editor documents open.

## Item operations

Right-click an item to rename it, delete it to the Windows Recycle Bin, reveal
it in File Explorer, or copy its project-relative or full Windows path. The
keyboard equivalents are `F2`, `Delete`, and `Ctrl+C` for the relative path.
Right-click selection follows the pointer, so the menu always acts on the item
that was clicked.

Drag a file or folder onto another folder, or onto a file already inside the
destination folder, to move it. Dropping onto a file places the moved item next
to that file. Moves stay within the active project, never overwrite a
destination item, and cannot place a folder inside itself or one of its
descendants. Declared project inputs and open documents retain the same
protection used by rename and delete.

Rename never overwrites another item. Removal always asks for confirmation and
uses the Recycle Bin so an accidentally removed item can be restored. Foundry
protects the active `.foundryproj`, every declared managed/native source,
target and test definitions, OBS design sources, component sources, publishing
licence/changelog files, and folders containing any such inputs. Update the
project design or manifest deliberately before changing those paths.

Every open editor tab has a visible close button. Closing an edited document
uses the same **Save**, **Don't Save**, or **Cancel** protection as **File →
Close Document**.

## Safety

Foundry accepts one file or folder name, not a path. The target must be an
existing directory inside the active project, cannot traverse a reparse point,
and cannot escape the project root. Existing files and folders are never
overwritten. Creation failures appear in Problems with a `CFW11xx` diagnostic.
