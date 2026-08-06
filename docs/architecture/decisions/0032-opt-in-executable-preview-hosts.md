# ADR 0032: Opt-in executable preview hosts

## Status

Accepted for Phase 22D on 2026-08-06.

## Decision

Keep the Phase 22A-C structural preview as the default. A creator must opt in
to live execution for each Design Preview session. Foundry stages bounded
copies of content and build artifacts beneath an owned disposable directory;
the desktop process never loads project code.

- Static web content runs in a disposable WebView2 profile. Requests outside
  the staged local content root, navigation, permissions, new windows,
  DevTools, context menus, autofill, and password storage are denied.
- WinForms builds first, then a copied managed assembly is loaded by a
  collectible context on an isolated STA host and captured to PNG.
- OBS plugins build first, then a copied native DLL is loaded by the existing
  crash-isolated libobs host. Foundry executes registration, source
  create/destroy, and property callbacks. Components without a standalone
  pixel surface retain the declared composition with verified live evidence.

All modes retain bounded request/result sizes, timeouts, process-tree
termination, logs, restart/stop controls, and owned-directory cleanup.

## Consequences

Live preview intentionally runs untrusted creator code and therefore carries a
clear warning and never persists as the default. The isolation reduces impact
but is not a security boundary equivalent to a virtual machine. Structural
preview remains available when live prerequisites are missing or execution
fails.
