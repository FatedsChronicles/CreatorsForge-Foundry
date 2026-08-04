# Privacy and offline behaviour

Creators Forge Foundry has no telemetry, advertising identifiers, analytics,
account requirement, or automatic upload path. Projects, settings, recent-file
history, build output, recovery snapshots, deployment receipts, update packages,
and failure reports remain on the local computer.

Network access is disabled by default. Foundry can operate offline for project
editing, managed builds with restored dependencies, packaging, testing, and
deployment. Network is used only after the creator enables it in Settings and
explicitly starts an OBS SDK download, update check, or update staging operation.

The OBS toolchain manager also accepts a folder containing both checksum-matched
official archives. Update manifests and packages can be local files, so both
workflows can be completed on an offline machine.

Failure reports contain the exception, stack trace, product/runtime version,
and operating-system version. They are saved locally and never transmitted. A
diagnostic bundle is created only on request. Local paths are redacted from its
system summary unless path inclusion is explicitly enabled. Always review a
bundle before sharing it.

Uninstall preserves settings and recovery data by default. The explicit
`-RemoveUserData` option removes that local state.

## Private alpha sharing

Invitation-only distribution does not add an account, telemetry, or automatic
support upload. If a tester chooses to share a diagnostic bundle, maintainers
receive only the reviewed files in that attachment. Bundles include a system
summary, bundle inventory, issue template, and local failure reports; they do
not include project source or recovery text. Testers control the sharing method
and should delete the attachment from the invited issue location when it is no
longer required. The project does not operate a separate diagnostic retention
service.
