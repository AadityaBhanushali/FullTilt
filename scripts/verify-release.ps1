param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$ExpectedVersion)
. "$PSScriptRoot/env.ps1"
$Archive = [IO.Path]::GetFullPath($Archive)
$expectedHash = ((Get-Content -LiteralPath (Join-Path (Split-Path -Parent $Archive) 'SHA256SUMS.txt')) -split ' ')[0]
if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Release archive checksum mismatch.' }
$archiveFile = [IO.Compression.ZipFile]::OpenRead($Archive)
try {
    $allowed = @('FullTilt/FullTilt.exe','FullTilt/Licenses/FullTilt-LICENSE.txt')
    foreach ($pack in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $allowed += "FullTilt/Licenses/runtime-notices/$pack/LICENSE.TXT"
        if ($pack -eq 'microsoft.netcore.app.runtime.win-x64' -or $archiveFile.Entries.FullName -contains "FullTilt/Licenses/runtime-notices/$pack/THIRD-PARTY-NOTICES.TXT") { $allowed += "FullTilt/Licenses/runtime-notices/$pack/THIRD-PARTY-NOTICES.TXT" }
    }
    foreach ($name in @('Turn-Off-Windows-Controller-Navigation.ps1','Turn-On-Windows-Controller-Navigation.ps1','README.txt')) { $allowed += "FullTilt/Step-1-Windows-Controller-Fix/$name" }
    $names = @($archiveFile.Entries | Where-Object { $_.Name.Length -gt 0 } | ForEach-Object { $_.FullName.Replace('\','/') })
    if (@(Compare-Object ($allowed | Sort-Object) ($names | Sort-Object)).Count -ne 0 -or $names.Count -ne $allowed.Count) { throw 'Archive contents differ from the approved allowlist.' }
    $destination = Join-Path $repoRoot ('artifacts/release-verification/' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $destination)
}
finally { $archiveFile.Dispose() }
$app = Join-Path $destination 'FullTilt'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $app 'Step-1-Windows-Controller-Fix') -File) {
    $source=Join-Path $repoRoot ('tools/Step-1-Windows-Controller-Fix/'+$file.Name)
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw 'Archived utility differs from reviewed source.' }
}
& "$PSScriptRoot/verify-privacy.ps1" -Path $app -VerifyRuntimeNotices
$exe = Join-Path $app 'FullTilt.exe'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$ExpectedVersion.0") { throw 'Archived executable has the wrong version.' }
# The ZIP carries no checksum file; compare against the verified published package instead.
$exeHash = ((Get-Content -LiteralPath (Join-Path $repoRoot "Finals/FullTilt-v$ExpectedVersion-win-x64/SHA256SUMS.txt")) -split ' ')[0]
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $exeHash) { throw 'Archived executable checksum mismatch.' }
# Simulate replacement with isolated profile files. Never read or write live settings here.
$profiles = Join-Path $app 'Profiles'
New-Item -ItemType Directory -Path $profiles -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'src/FullTilt.Core/Defaults/DefaultProfile.json') -Destination (Join-Path $profiles 'settings.json')
[IO.File]::WriteAllText((Join-Path $profiles 'app-preferences.json'), '{"Version":1,"Theme":"Light","AutomaticallyCheckForUpdates":false}')
$before = @(Get-ChildItem -LiteralPath $profiles -File | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
[IO.Compression.ZipFile]::ExtractToDirectory($Archive, $destination, $true)
$after = @(Get-ChildItem -LiteralPath $profiles -File | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
if (@(Compare-Object $before $after).Count -ne 0) { throw 'Application replacement changed isolated settings.' }
$output = Join-Path $destination 'diagnostics'
$previousCache = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $destination 'fresh-bundle-cache'
    $process = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', ('"{0}"' -f $output), '--public-launch-only') -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw "Extracted portable smoke test failed. See $output" }
}
finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousCache }
if (-not (Test-Path -LiteralPath (Join-Path $output 'public-launch.txt'))) { throw 'Extracted portable report is missing.' }
Write-Host 'PASS: release ZIP allowlist, hashes, version, isolated settings replacement and extracted single-file runtime smoke test.'
