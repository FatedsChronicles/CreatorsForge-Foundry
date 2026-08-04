# Private alpha compatibility matrix

Published 2026-07-29. “Verified” means the automated Foundry gate and the stated
real-host acceptance both passed; it does not imply compatibility with every
future prerelease or OBS 32.x update.

| Provider | Host | Build/package | Test evidence | Deployment/runtime evidence | Status |
|---|---|---|---|---|---|
| Streamer.bot | 1.0.4 stable | Managed DLL and stable-v23 import | args/CPH mock matrix | DLL, import, compile, action execution, health and lifecycle accepted | Verified |
| Streamer.bot | 1.0.5-alpha.34 | Same deterministic package contract | args/CPH mock matrix | DLL, import, compile, action execution and health accepted | Verified |
| Streamer.bot | 1.0.5-beta.1 | Same deterministic package contract | args/CPH mock matrix | DLL, import, compile, action execution and health accepted | Verified |
| OBS Studio | 32.1.2 Windows x64 | Pinned SDK 32.1.2 native package | ABI inspection and crash-isolated create/destroy lifecycle | Install, filter persistence, clean restart/shutdown, health and full deployment lifecycle accepted | Verified |

Streamer.bot 1.0.4 warns before importing an export produced by a 1.0.5 alpha or
beta host; continuing the reviewed import succeeded. OBS 32.1.1 proved the
earlier minimal module-load spike, but the SDK-backed filter and complete
deployment gate are published only for 32.1.2. Other OBS 32.x versions are not
yet private-alpha supported targets.

