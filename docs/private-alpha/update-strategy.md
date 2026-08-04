# Private alpha update strategy

Private alpha versions are immutable and use `0.15.0-alpha.N`. A larger `N` is
newer; a stable `0.15.0` release is newer than every `0.15.0-alpha.N` build.
Packages are never replaced in place.

Update checks remain manual. Invited testers receive an access-controlled local
or HTTPS `foundry-update.json` location. HTTPS access is opt-in under Settings;
a local manifest works offline. Foundry verifies package size and SHA-256,
stages the archive, and never installs silently.

Before updating, close active hosts, save projects, and retain the previous
approved bundle. Stage the update in Foundry, close Foundry, extract the package,
and run its installer. The installer retains the previous application until the
replacement succeeds. Settings and recovery state stay outside the install
folder and must remain intact.

If a build is withdrawn, maintainers remove its manifest from the invited
channel and notify testers with the last approved version and its separately
communicated manifest hash. Downgrade only from that retained approved bundle.

