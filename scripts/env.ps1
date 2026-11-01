$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.build/dotnet'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.build/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$buildTemp = Join-Path $repoRoot '.build/tmp'
New-Item -ItemType Directory -Path $buildTemp -Force | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $repoRoot '.build/bundle-cache'
Set-Location -LiteralPath $repoRoot
function Assert-NativeSuccess([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE" } }
function Get-FullTiltVersion {
    $value = [string](([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/FullTilt/FullTilt.csproj') -Raw)).Project.PropertyGroup.Version | Select-Object -First 1)
    if ($value -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Application Version must be a stable numeric MAJOR.MINOR.PATCH.' }
    $parsedVersion = [Version]$value
    if ($parsedVersion.Major -gt 65534 -or $parsedVersion.Minor -gt 65534 -or $parsedVersion.Build -gt 65534) { throw 'Version exceeds Windows assembly metadata limits.' }
    return $value
}
