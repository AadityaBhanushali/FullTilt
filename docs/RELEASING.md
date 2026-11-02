# Releasing FullTilt

Maintainer notes for cutting a release. Contributors don't need this.

- **Version**: the single `<Version>` in `src/FullTilt/FullTilt.csproj`. The app header, About box, Settings and the update checker all read it. Tags are `vMAJOR.MINOR.PATCH` and must match.
- **Assets**: `FullTilt-win-x64.zip`, `SHA256SUMS.txt` and `RELEASE_NOTES.md`.
- **Update integration**: the in-app update check reads the latest stable GitHub release. Repository and creator links live in `src/FullTilt/Assets/LaunchLinks.json`.
- **License**: MIT. Microsoft .NET runtime licenses ship inside each ZIP (see `THIRD-PARTY-NOTICES.md`).

## Prepare

Prerequisites: Windows 11, PowerShell 7, Git and the .NET SDK from `global.json`.

1. Update the version in `src/FullTilt/FullTilt.csproj`.
2. Add a `## [X.Y.Z] - YYYY-MM-DD` section with real user-facing notes to `CHANGELOG.md`. Release validation fails on an empty section.
3. Validate and build everything locally:

   ```powershell
   ./scripts/validate-release.ps1 -Tag vX.Y.Z
   ./scripts/release.ps1 -Tag vX.Y.Z
   ```

   `release.ps1` runs the full Release build and tests, publishes a self-contained untrimmed single-file x64 executable, runs the packaged diagnostics, writes the ZIP, checksums and notes, and smoke-tests the extracted result. Output goes to `Finals/FullTilt-vX.Y.Z-win-x64` and `artifacts/github-release/vX.Y.Z/` (both git-ignored). An existing package is snapshotted before it is replaced, and `Profiles` is preserved.

   Use `./scripts/release.ps1 -PackageOnly` to repackage after a successful build, publish and `./scripts/verify-package.ps1`.

## Publish

Pushing to any branch runs [`build.yml`](../.github/workflows/build.yml): build, tests, publish and package verification with read-only permissions.

Pushing a stable tag runs [`release.yml`](../.github/workflows/release.yml):

1. Validate that the tag, project version and changelog agree.
2. Build, test and package on a Windows runner with read-only access.
3. Upload only the three release assets to a separate job that holds `contents: write`.
4. That job creates a **draft**, attaches every asset, checks their presence and size, and only then publishes it as the latest release. A failure before that point leaves a draft or nothing, never a partial download.

```powershell
git tag -a vX.Y.Z -m "FullTilt vX.Y.Z"
git push origin main vX.Y.Z
```

Never move or overwrite a published tag or release asset. No personal token is needed: the workflow uses the built-in `GITHUB_TOKEN`, and all third-party Actions are pinned to commit SHAs.

After the workflow finishes, check the release page: the three assets, the notes, and that `https://github.com/adityabhanushali-arch/FullTilt/releases/latest` points at the new version.

## What stays out of releases

Personal `Profiles`, logs, credentials, build outputs and archives are git-ignored. The ZIP is built from an explicit allowlist (`scripts/release.ps1`, checked by `scripts/verify-release.ps1`), and `scripts/verify-privacy.ps1` scans the source tree and packages for private paths and identities.

## Known limits

Local verification can't prove a GitHub-hosted workflow run, physical controller behavior or code signing. The application is unsigned.
