# Security policy

## Supported versions

Security fixes go into the latest stable release. Please update before reporting.

## Reporting a vulnerability

Use GitHub's private reporting: open the [Security tab](https://github.com/AadityaBhanushali/FullTilt/security/advisories/new) and choose **Report a vulnerability**. Please don't file public issues for security problems.

Include the FullTilt version, Windows version, what you observed and, if possible, steps to reproduce. You can expect an acknowledgement within a few days. FullTilt is maintained by one person, so fixes are made on a best-effort basis.

## What is in scope

FullTilt sends synthetic mouse and keyboard input, runs a low-level keyboard hook only while its shortcut recorder is open, reads and writes files in its own `Profiles` folder, and makes anonymous requests to GitHub's public releases API for update checks. It has no accounts, telemetry or server component. Reports about those areas, the release pipeline, or the `Step-1-Windows-Controller-Fix` registry files are welcome.

## Verifying downloads

Releases are built by GitHub Actions from a tagged commit. Each release includes `SHA256SUMS.txt`. The executable is **not code-signed**, so Windows SmartScreen may show a reputation prompt.
