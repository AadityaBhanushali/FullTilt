param([switch]$PackageOnly, [string]$Tag, [string]$OutputDirectory)
. "$PSScriptRoot/env.ps1"
$version = Get-FullTiltVersion
if (-not $Tag) { $Tag = "v$version" }
& "$PSScriptRoot/validate-release.ps1" -Tag $Tag
# Local preparation only. Remote publication exists only in the tag-triggered workflow.
if (-not $PackageOnly) {
    & "$PSScriptRoot/build.ps1"
    & "$PSScriptRoot/publish.ps1" -SkipBuild
    & "$PSScriptRoot/verify-package.ps1"
}
$package = Join-Path $repoRoot "Finals/FullTilt-v$version-win-x64"
$exe = Join-Path $package 'FullTilt.exe'
$proof = Get-Content -LiteralPath (Join-Path $package 'verification.json') -Raw | ConvertFrom-Json
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($proof.Version -ne $version -or $proof.ExecutableSha256 -ne $exeHash) { throw 'The binary has no matching successful package verification. Run verify-package.ps1.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Published executable and source metadata differ.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('artifacts/github-release/v' + $version + '/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$staging = Join-Path $output 'FullTilt'
$zip = Join-Path $output 'FullTilt-win-x64.zip'
if (Test-Path -LiteralPath $zip) { throw 'Refusing to replace an existing release ZIP. Select a new output directory.' }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
# Explicit allowlist: never recurse over Finals, which can contain personal Profiles.
# Users get only the app, the Step 1 fix and the licenses the bundled runtime requires.
Copy-Item -LiteralPath (Join-Path $package 'FullTilt.exe') -Destination $staging
$licenses = Join-Path $staging 'Licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $licenses 'FullTilt-LICENSE.txt')
$notices = Join-Path $licenses 'runtime-notices'
$utility = Join-Path $staging 'Step-1-Windows-Controller-Fix'
New-Item -ItemType Directory -Path $utility -Force | Out-Null
foreach ($name in @('Disable_Windows_Controller_UI_Navigation.reg','Restore_Windows_Default_Controller_Navigation.reg','README.txt')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "tools/Step-1-Windows-Controller-Fix/$name") -Destination $utility -ErrorAction Stop
}
$runtimePacks = Get-Content -LiteralPath (Join-Path $package 'runtime-packs.json') -Raw | ConvertFrom-Json
foreach ($runtimePack in $runtimePacks) {
    $packName = $runtimePack.Name
    if ($packName -notin @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) { throw 'Unexpected runtime pack.' }
    $pack = Get-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES ($packName + '/' + $runtimePack.Version)) -ErrorAction Stop
    $target = Join-Path $notices $packName
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $license = if (Test-Path -LiteralPath (Join-Path $pack.FullName 'LICENSE.TXT')) { Join-Path $pack.FullName 'LICENSE.TXT' } else { Join-Path $pack.FullName 'LICENSE' }
    Copy-Item -LiteralPath $license -Destination (Join-Path $target 'LICENSE.TXT') -ErrorAction Stop
    $thirdParty = Join-Path $pack.FullName 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $thirdParty) { Copy-Item -LiteralPath $thirdParty -Destination (Join-Path $target 'THIRD-PARTY-NOTICES.TXT') -ErrorAction Stop }
}
$changelog = Get-Content -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Raw
$notes = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($version) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## |\z)').Groups['notes'].Value.Trim()
[IO.File]::WriteAllText((Join-Path $output 'RELEASE_NOTES.md'), $notes + "`n")
Compress-Archive -LiteralPath $staging -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$zipHash  FullTilt-win-x64.zip`n")
& "$PSScriptRoot/verify-release.ps1" -Archive $zip -ExpectedVersion $version
Write-Host "Prepared local release: $zip"
if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "assets=$output" }
