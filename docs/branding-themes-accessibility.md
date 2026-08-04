# Phase 18 — Branding, themes, and visual accessibility

## Status

Phase 18 implementation is complete. Product-owner visual acceptance remains
required before the phase is closed.

## Branding

The supplied Creator Forge SVG is retained without AI alteration and loaded as
vector geometry for lossless WPF scaling. A multi-resolution Windows ICO is
generated from the same SVG source. The branding is used by the executable,
every Foundry window, and the main workspace header.

## Themes

**Tools > Settings > Appearance** provides three persisted choices:

- **System** follows the current Windows app-theme preference and responds to
  Windows preference changes while Foundry is running;
- **Dark** uses the Foundry dark palette;
- **Light** uses the Foundry light palette.

The selected theme applies immediately after **Save** and is restored before
the first window is shown at the next launch. Existing settings files migrate
implicitly: a missing or unknown theme resolves safely to **System**.

Windows High Contrast takes priority over the selected theme. Foundry uses the
system window, text, highlight, control, and menu brushes while High Contrast
is active and restores the selected preference when it is disabled.

## Contrast and control states

All application XAML now consumes live semantic brushes rather than embedding
dialog-specific foreground or background colours. Foundry owns the foreground,
background, hover, selected, focused, pressed, and disabled states for menus,
buttons, tabs, tree/list items, combo-box items, data grids, inputs, and
tooltips. This prevents the active Windows theme from introducing unreadable
white-on-white or black-on-dark combinations.

Automated contrast checks require at least 4.5:1 for normal text across primary,
muted, accent, error, editor, button, and selection combinations in both fixed
palettes.

The dark-theme acceptance correction explicitly applies the Foundry window
style to every WPF window subclass, owns the complete tab header template, maps
stock Windows control resources into the active palette, and uses a dedicated
readable dark C-family syntax definition in AvalonEdit.

The syntax definition is validated by the desktop smoke gate. If a packaged
highlighting resource is ever invalid, the editor falls back to readable plain
theme text instead of allowing a deferred layout exception to block the UI.

## Manual exit gate

Using the latest isolated Phase 18 build:

1. Confirm the Creator Forge logo appears on the executable, taskbar/window,
   and workspace header.
2. Select **Dark**, save, and inspect every main pane, menu and menu hover,
   toolbar button and hover, project tree selection, document tab, editor,
   output/problems tabs, Settings, Test Explorer, deployment, publishing,
   snippets, designers, and package viewer.
3. Repeat the same inspection with **Light**.
4. Select **System**, change the Windows app colour mode, and confirm an open
   Foundry window follows it without losing readable text.
5. Restart Foundry and confirm the selected preference persists.
6. Enable Windows High Contrast and confirm system colours override Foundry's
   palette; disable it and confirm the selected theme returns.
7. Navigate the menu, Settings tabs, controls, and dialogs using the keyboard.
   Confirm focus remains visible and no information depends on colour alone.

Phase 18 passes only after Dark, Light, System, High Contrast, restart
persistence, branding, and keyboard/focus checks are product-owner verified.
