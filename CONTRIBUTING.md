# Contributing to FullTilt

Open an issue for a reproducible bug or a focused feature proposal before making a large change. Contributions should keep FullTilt lightweight and preserve existing controller bindings, profiles, input cleanup and UI behavior.

## Project layout

| Path | What lives there |
| --- | --- |
| `src/FullTilt.Core` | Deterministic logic with no UI or native calls: settings, profiles, actions, pie menus, motion math, update checks. Most changes belong here. |
| `src/FullTilt.Windows` | Windows adapters: XInput, synthetic keyboard and mouse input, window actions, the touch keyboard, startup registration. |
| `src/FullTilt` | The WPF application: pages, pie overlay, dialogs, theme, and the packaged `--smoke-test` diagnostics. |
| `tests/FullTilt.Tests` | Unit tests against fake controllers, fake input sinks and isolated settings. |
| `scripts` | Build, publish, verify and release scripts. |
| `tools/Step-1-Windows-Controller-Fix` | The two scripts that turn Windows' controller navigation off and back on. They ship in the release ZIP under the same folder name. |
| `docs` | [User guide](docs/USER-GUIDE.md), [release process](docs/RELEASING.md) and the screenshots and animations used in the README. |

## Build and test

Use Windows 11 x64, PowerShell 7, Git and the .NET SDK specified in `global.json`. Run `./scripts/build.ps1`. The suite uses fake input/controller adapters and isolated settings. Do not run live emulation, inject real input, edit user Profiles, or manipulate another application's windows in automated tests. Put deterministic behavior in FullTilt.Core and native input behind Windows adapters. Do not trim WPF publications or introduce dependencies without need.

Use `./scripts/release.ps1` for the complete local portable build and packaging check. It creates no remote tags or releases. Physical controller, target application, focus and multi-monitor checks are separate manual validation. Record their scope honestly.

## Pull requests

Explain the concrete problem, resulting behavior and relevant validation. Include focused regression coverage where meaningful. Preserve settings compatibility and original-file backups. Never submit personal settings, logs with private information, credentials, generated packages or build outputs. The bundled defaults in `src/FullTilt.Core/Defaults/DefaultProfile.json` are deliberate; don't replace them with a personal configuration.

By submitting a contribution, you agree that it is provided under the repository's MIT License. Confirm permission to include any artwork or third-party material. FullTilt's logo and controller illustration are credited in THIRD-PARTY-NOTICES.md.

## Releases

Only the maintainer chooses versions and publishes releases. The process is described in [docs/RELEASING.md](docs/RELEASING.md).
