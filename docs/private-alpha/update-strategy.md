# Private alpha update strategy

Private alpha versions are immutable and use `0.15.0-alpha.N`. A larger `N` is
newer; a stable `0.15.0` release is newer than every `0.15.0-alpha.N` build.
Packages are never replaced in place.

Update checks remain manual. Testers select **Prerelease (includes stable)** in
Settings and explicitly enable HTTPS access. Foundry queries only the official
GitHub releases feed, excludes drafts, selects the highest semantic version with
an uploaded `foundry-update.json`, and then verifies the updater's declared size
and SHA-256. A local or custom manifest remains available for access-controlled
or offline distribution and is not affected by the channel selector.

Before updating, close active hosts, save projects, and retain the previous
approved bundle. Stage the update in Foundry and launch the verified native
updater. The installer retains the previous application until the
replacement succeeds. Settings and recovery state stay outside the install
folder and must remain intact.

If a build is withdrawn, maintainers remove its published prerelease and notify
testers with the last approved version and its separately communicated manifest
hash. Downgrade only from that retained approved bundle.

