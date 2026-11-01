# Preparing and publishing FullTilt releases

Target repository: `adityabhanushali-arch/FullTilt` (public). Description: **Use a game controller as a mouse and keyboard on Windows. Built for couch navigation and alternative desktop input.** License: MIT. Initial public version: 1.0.0. Distribution: `FullTilt-win-x64.zip` plus `SHA256SUMS.txt` and `RELEASE_NOTES.md`.

## Local preparation when the user says “Prepare FullTilt v1.1.0 for release”

1. Inspect Git status, existing branches, release evidence and changes since the previous public tag. Preserve relevant uncommitted work using reviewed paths and the configured identity. Keep prior release branches and packages fixed; create/reuse a suitable `codex/` release branch without merging.
2. Review `git log` and the diff since the previous public tag. Update the sole `<Version>` in `src/FullTilt/FullTilt.csproj` to the explicitly requested numeric version. Header, About, Settings, executable version and update comparison derive from this metadata. Do not change profile schema merely for a release number.
3. Add a meaningful user-facing section to CHANGELOG.md describing actual changes, relevant limitations and installation. Avoid placeholders. Update existing guides where behavior changed. Keep update repository and creator links in `src/FullTilt/Assets/LaunchLinks.json`.
4. Run `./scripts/validate-release.ps1 -Tag v1.1.0`, then `./scripts/release.ps1 -Tag v1.1.0`. This runs the full Release build/tests, self-contained x64 untrimmed single-file publish, full isolated packaged diagnostics, ZIP/checksum/notes generation, allowlist checks, isolated settings replacement and an extracted-runtime smoke test. Publication snapshots an existing package under artifacts and preserves its Profiles. One Final folder is used per numeric public version.
5. Review the actual staged source/docs/assets and secret scan findings, commit with the configured identity, and show the commit, changes, archive/checksum, test results and remaining manual validation. Do not create or push any tag or remote changes during preparation. Ask for explicit approval of that exact commit/version before publishing.

Use `./scripts/release.ps1 -PackageOnly` only after a current successful build/publish/verify. Its verification proof must match the packaged executable's hash and version. Archives live under `artifacts/github-release/vVERSION/`; runtime Profiles are excluded. Production runtime does not inherit build-script temporary/cache overrides. The application remains unsigned.

## First publication: actions only after explicit approval

Prerequisites: Windows/PowerShell 7/Git/.NET SDK from `global.json`. GitHub CLI is installed and authenticated as adityabhanushali-arch with repository/workflow access. Recheck using gh auth status and gh api user before publication; if PATH has not refreshed, use the installed CLI by its full path. Do not put a personal token in the application, repository or workflow. GitHub-hosted Windows runners provide gh and receive the built-in token only in the publishing step.

Codex will recheck the approved commit, clean state, archive checksums, update target and remote authentication. If the empty repository does not exist, create it **after approval**, without a generated README/license or template that would conflict with this prepared source:

```powershell
gh auth status
gh api user --jq .login
gh auth setup-git
gh repo create adityabhanushali-arch/FullTilt --public --description "Use a game controller as a mouse and keyboard on Windows. Built for couch navigation and alternative desktop input."
git remote add origin https://github.com/adityabhanushali-arch/FullTilt.git
```

Authentication may require user interaction. If origin or the remote already exists, inspect it first and use it only if it is the approved target. Never overwrite a populated remote or force-push. Upload only the approved branch and tag; **do not push all local branches/tags**, which include development history.

```powershell
git push origin APPROVED_COMMIT:refs/heads/main
# Wait for the hosted Build and test workflow for this commit to succeed.
git tag -a v1.0.0 APPROVED_COMMIT -m "FullTilt v1.0.0"
git push origin refs/tags/v1.0.0
```

Use the verified literal commit hash in place of APPROVED_COMMIT. Do not execute the placeholder. Confirm no existing local/remote tag has that name. Push main first and inspect its hosted build result before creating/pushing the release tag. For subsequent releases use the approved version/commit, after checking the current remote main ancestry. Never move a previously published tag.

The branch push runs `.github/workflows/build.yml` with read-only permissions and no release capability. The tag push runs `.github/workflows/release.yml`: validates stable tag/version/changelog, builds/tests/packages on Windows with read-only contents permission, uploads only the three release assets, then passes them to a separate job with **contents: write**. That job creates a draft, attaches all assets, verifies their presence/size and only then publishes it as the latest stable release. All third-party Actions are official actions pinned to reviewed commit SHAs. No PAT, paid service or runtime account is needed. See [GitHub token permissions](https://docs.github.com/en/actions/tutorials/authenticate-with-github_token) and the [GitHub CLI draft/publish flags](https://cli.github.com/manual/gh_release_edit).

Codex will use `gh run list` / `gh run watch` to follow both workflows, inspect failures, and then verify the published asset hashes, release notes and anonymous `releases/latest` response. A failure before final publication leaves a draft or no release, not an advertised partial download. Existing releases are never overwritten by the script. If a failed attempt leaves a draft, inspect it and obtain authorization for cleanup/resumption rather than deleting it silently. Organization policies or Actions being disabled may need maintainer action; no such remote settings have been changed during local preparation.

## Development-to-public version transition

Development “4.8” actually has numeric assembly version 1.4.8. Numeric comparison correctly sees public 1.0.0 as older, so that development build will not offer 1.0.0 as an update. Install public 1.0.0 manually, exiting 4.8 first and copying its entire Profiles folder into the new directory before first launch. Keep the old 4.8 package as rollback. Settings stay at schema 17 and retain existing values. Public 1.0.0 detects future stable public versions such as 1.1.0 normally. No special version coercion, settings rewrite or forced downgrade is added.

## Distribution review

The creator confirmed ownership and use of logo/controller artwork on 2026-11-01. MIT covers repository-authored assets and code; Microsoft runtime licenses/notices ship separately. Screenshots in docs/screenshots are renders of the actual application with the approved bundled defaults and fake/no input service, not marketing mockups or hardware-test evidence. Source/filename/history pattern scans are useful but not a guarantee that all private data has been detected. Public commits use 351214207+adityabhanushali-arch@users.noreply.github.com. Prior local history is backed up separately and must never be pushed. Use only the isolated public source repository described below. Existing local packages, personal Profiles, diagnostics and secret files stay ignored and out of release assets.

Local verification cannot establish a GitHub-hosted workflow run, first public endpoint, physical controller acceptance or code signing. Record those limits in the handoff. No live controller emulation is part of the automated release process.

## Clean public history boundary

The only repository authorized for eventual publication is the isolated local repository at artifacts/public-source-1.0.0, on main, with its sanitized root and reviewed public-only follow-up commits. The development checkout and pre-privacy-history.bundle contain private historical identities; never add a remote or push from them. Never use --mirror, --all, --tags, or import/fetch old local refs into the public repository. Run all publication commands from the isolated repository, after approval of its exact root commit. Before pushing, verify the only root is 92bca02970f07e95e23c31b43eeb2dd2732de50e and every reachable commit uses the approved noreply identity, verify author/committer noreply identities, and check there are no tags, remotes, alternates or replace refs. Production Release builds disable symbols, map source paths and omit inherited development revision metadata. verify-privacy.ps1 gates both publication and extracted archives.

## Optional controller navigation utility

Every Windows release copies the explicit reviewed file list from FullTilt-Controller-Navigation-Fix beside the executable. ZIP verification requires those exact filenames, checks their bytes against source, and never executes the registry entry points. test-navigation-fix.ps1 exercises the shared implementation only with a fake backend, including absent/existing original values, types, repeated disable, backup failure, restoration failure and foreign-machine rejection. The original Finals utility remains untouched. No raw registry writes are active in the retained reference .reg files. These utility files are part of the repository MIT license.
