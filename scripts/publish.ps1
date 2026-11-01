param([switch]$SkipBuild)
. "$PSScriptRoot/env.ps1"
if (-not $SkipBuild) { & "$PSScriptRoot/build.ps1" }
$version = Get-FullTiltVersion
$destination = Join-Path $repoRoot "Finals/FullTilt-v$version-win-x64"
$publicationId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$staging = Join-Path $repoRoot ('artifacts/publish/' + $version + '/' + $publicationId)
dotnet publish src/FullTilt/FullTilt.csproj -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -p:RestoreConfigFile="$repoRoot/NuGet.Config" -o $staging
Assert-NativeSuccess 'Self-contained single-file publish'
& "$PSScriptRoot/verify-privacy.ps1" -Path $staging
$targetFramework = [string](([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/FullTilt/FullTilt.csproj') -Raw)).Project.PropertyGroup.TargetFramework)
Get-ChildItem -LiteralPath (Join-Path $repoRoot "src/FullTilt/bin/Release/$targetFramework/win-x64") -Filter 'FullTilt*.dll' | ForEach-Object { & "$PSScriptRoot/verify-privacy.ps1" -Path $_.FullName -ManagedAssemblies }
$runtimeConfig = Get-Content -LiteralPath (Join-Path $repoRoot "src/FullTilt/bin/Release/$targetFramework/win-x64/FullTilt.runtimeconfig.json") -Raw | ConvertFrom-Json
$runtimePacks = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object { [pscustomobject]@{ Name=($_.name.ToLowerInvariant() + '.runtime.win-x64'); Version=$_.version } })
if ($runtimePacks.Count -ne 2) { throw 'Expected .NET and Windows Desktop runtime frameworks in self-contained publication.' }
$executable = Join-Path $destination 'FullTilt.exe'
$stagedExecutable = Join-Path $staging 'FullTilt.exe'
if (-not (Test-Path -LiteralPath $stagedExecutable)) { throw 'Published executable is missing.' }
# Reuse one folder for this version. Preserve the entire previous package outside Finals,
# then replace only the executable atomically; a running/locked copy leaves the old exe intact.
if (Test-Path -LiteralPath $executable) {
    $backup = Join-Path $repoRoot ('artifacts/package-backups/' + $version + '/' + $publicationId)
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup -Recurse -Force -ErrorAction Stop
    Write-Host "Previous package preserved: $backup"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$replacement = Join-Path $destination ('FullTilt.exe.' + $publicationId + '.tmp')
try {
    Copy-Item -LiteralPath $stagedExecutable -Destination $replacement -ErrorAction Stop
    if (Test-Path -LiteralPath $executable) { [System.IO.File]::Replace($replacement, $executable, (Join-Path $staging 'FullTilt.previous.exe')) }
    else { [System.IO.File]::Move($replacement, $executable) }
}
finally { if (Test-Path -LiteralPath $replacement) { Remove-Item -LiteralPath $replacement -ErrorAction SilentlyContinue } }
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $destination 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'CHANGELOG.md') -Destination $destination
$runtimePacks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'runtime-packs.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -Destination (Join-Path $destination 'AGENTS.md')
# Keep user profile data intact when republishing this version.
New-Item -ItemType Directory -Path (Join-Path $destination 'Profiles') -Force | Out-Null
$packageDocs = Join-Path $destination 'docs'
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/PRODUCT.md'), (Join-Path $repoRoot 'docs/STATUS.md') -Destination $packageDocs
$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
[System.IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'), "$($hash.Hash.ToLowerInvariant())  FullTilt.exe`n")
Write-Host "Published: $executable"
Write-Host "Bytes: $((Get-Item -LiteralPath $executable).Length)"
