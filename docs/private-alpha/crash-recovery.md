# Crash-recovery review

Foundry writes unsaved editor snapshots to local application data. On opening a
document, a newer snapshot is restored into the editor so it can be reviewed and
saved. Recovery is a safety net, not version control.

Private-alpha rehearsal:

1. Open a disposable copy of a sample source file, edit it, and wait longer than
   the configured autosave interval without saving.
2. End Foundry, relaunch it, and reopen the project and document.
3. Confirm the newer unsaved text returns, then save or deliberately discard it.
4. Open **Tools > Recovery and Diagnostics** and confirm the local recovery and
   failure inventory remains available.
5. Create a diagnostic bundle. Confirm it contains a bundle manifest, system
   summary, issue template, and any failure reports, but no project source or
   recovery text.
6. Confirm local paths are redacted unless the privacy option was explicitly
   enabled.
7. Uninstall Foundry normally and confirm recovery/settings remain. Use the
   explicit remove-user-data option only for the final cleanup rehearsal.

Corrupt or unreadable snapshots are ignored rather than blocking startup. If
recovery does not appear, preserve the failure report and record the document's
last saved state in the issue template.

