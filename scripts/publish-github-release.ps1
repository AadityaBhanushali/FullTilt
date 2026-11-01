param([Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][string]$Assets, [switch]$Publish)
. "$PSScriptRoot/env.ps1"
& "$PSScriptRoot/validate-release.ps1" -Tag $Tag
$repository = 'adityabhanushali-arch/FullTilt'
$Assets = [IO.Path]::GetFullPath($Assets)
foreach ($file in @('FullTilt-win-x64.zip','SHA256SUMS.txt','RELEASE_NOTES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Assets $file))) { throw "Release asset is missing: $file" }
}
$expected = ((Get-Content -LiteralPath (Join-Path $Assets 'SHA256SUMS.txt')) -split ' ')[0]
if ((Get-FileHash -LiteralPath (Join-Path $Assets 'FullTilt-win-x64.zip') -Algorithm SHA256).Hash -ne $expected) { throw 'Release ZIP checksum mismatch.' }
if (-not $Publish) {
    Write-Host "DRY RUN: create draft FullTilt $Tag in $repository; attach ZIP, checksum and notes; verify assets; publish as latest stable. No remote request made."
    return
}
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_EVENT_NAME -ne 'push' -or $env:GITHUB_REPOSITORY -cne $repository -or $env:GITHUB_REF -cne "refs/tags/$Tag" -or -not $env:GH_TOKEN) {
    throw 'Publication is restricted to the approved tag-push GitHub Actions workflow.'
}
$head = git rev-parse HEAD
Assert-NativeSuccess 'Read release commit'
if ($head -cne $env:GITHUB_SHA) { throw 'Checkout and triggering tag commit differ.' }
gh release create $Tag --repo $repository --draft --verify-tag --title "FullTilt $Tag" --notes-file (Join-Path $Assets 'RELEASE_NOTES.md')
Assert-NativeSuccess 'Create draft release'
gh release upload $Tag (Join-Path $Assets 'FullTilt-win-x64.zip') (Join-Path $Assets 'SHA256SUMS.txt') (Join-Path $Assets 'RELEASE_NOTES.md') --repo $repository
Assert-NativeSuccess 'Attach release assets'
$releaseJson = gh release view $Tag --repo $repository --json isDraft,assets
Assert-NativeSuccess 'Verify draft assets'
$release = $releaseJson | ConvertFrom-Json
if (-not $release.isDraft) { throw 'Expected a draft release before publication.' }
foreach ($name in @('FullTilt-win-x64.zip','SHA256SUMS.txt','RELEASE_NOTES.md')) {
    $asset = @($release.assets | Where-Object name -ceq $name)
    if ($asset.Count -ne 1 -or $asset[0].size -ne (Get-Item -LiteralPath (Join-Path $Assets $name)).Length) { throw "Incomplete draft release asset: $name" }
}
gh release edit $Tag --repo $repository --draft=false --prerelease=false --latest
Assert-NativeSuccess 'Publish complete stable release'
