param([string]$Tag)
. "$PSScriptRoot/env.ps1"
$version = Get-FullTiltVersion
if (-not $Tag) { $Tag = "v$version" }
if ($Tag -cne "v$version") { throw "Release tag '$Tag' must exactly match stable application version v$version." }
$links = Get-Content -LiteralPath (Join-Path $repoRoot 'src/FullTilt/Assets/LaunchLinks.json') -Raw | ConvertFrom-Json
if ($links.GitHubOwner -cne 'AadityaBhanushali' -or $links.GitHubRepository -cne 'FullTilt') { throw 'Unexpected update repository.' }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'LICENSE'))) { throw 'The approved LICENSE is required.' }
$changelog = Get-Content -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Raw
$section = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($version) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## |\z)')
if (-not $section.Success -or [string]::IsNullOrWhiteSpace($section.Groups['notes'].Value)) { throw "CHANGELOG.md needs user-facing notes for [$version]." }
Write-Host "Validated $Tag, metadata, repository and changelog. No tag or remote changes made."
