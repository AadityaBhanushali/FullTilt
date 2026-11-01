# Local failure-path tests. Git/gh are scoped fakes; no GitHub request is possible.
. "$PSScriptRoot/env.ps1"
$version = Get-FullTiltVersion
$testAssets = Join-Path $repoRoot ('artifacts/release-script-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testAssets -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $testAssets 'FullTilt-win-x64.zip'), 'Local CLI fixture, not a distributable application.')
[IO.File]::WriteAllText((Join-Path $testAssets 'RELEASE_NOTES.md'), 'Local release-script test fixture.')
$hash = (Get-FileHash -LiteralPath (Join-Path $testAssets 'FullTilt-win-x64.zip') -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $testAssets 'SHA256SUMS.txt'), "$hash  FullTilt-win-x64.zip`n")
$testState = [pscustomobject]@{ Calls=[Collections.Generic.List[string]]::new(); Incomplete=$false; FailUpload=$false }
function git { $global:LASTEXITCODE=0; return 'local-test-commit' }
function gh {
    $global:LASTEXITCODE=0
    $operation = [string]$args[1]; $testState.Calls.Add($operation)
    if ($operation -eq 'upload' -and $testState.FailUpload) { $global:LASTEXITCODE=1; return }
    if ($operation -eq 'view') {
        $assets = @('FullTilt-win-x64.zip','SHA256SUMS.txt','RELEASE_NOTES.md') | ForEach-Object { @{name=$_;size=(Get-Item -LiteralPath (Join-Path $testAssets $_)).Length} }
        if ($testState.Incomplete) { $assets = @($assets | Select-Object -First 1) }
        return (@{isDraft=$true;assets=@($assets)} | ConvertTo-Json -Depth 5 -Compress)
    }
}
function Expect-Failure([scriptblock]$Action, [string]$Name) {
    $failed=$false; try { & $Action } catch { $failed=$true }
    if (-not $failed) { throw "Expected failure: $Name" }; Write-Host "PASS: $Name"
}
$variables = @('GITHUB_ACTIONS','GITHUB_EVENT_NAME','GITHUB_REPOSITORY','GITHUB_REF','GITHUB_SHA','GH_TOKEN')
$savedEnvironment = @{}; foreach ($name in $variables) { $savedEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
$savedExitCode=$LASTEXITCODE
try {
    Expect-Failure { & "$PSScriptRoot/validate-release.ps1" -Tag 'not-a-stable-tag' } 'mismatched/nonsemantic tag rejected'
    & "$PSScriptRoot/publish-github-release.ps1" -Tag "v$version" -Assets $testAssets
    if ($testState.Calls.Count -ne 0) { throw 'Dry run contacted gh.' }; Write-Host 'PASS: default publication plan performs no remote request'
    $env:GITHUB_ACTIONS='false'
    Expect-Failure { & "$PSScriptRoot/publish-github-release.ps1" -Tag "v$version" -Assets $testAssets -Publish } 'publication outside approved CI context rejected'
    $env:GITHUB_ACTIONS='true'; $env:GITHUB_EVENT_NAME='push'; $env:GITHUB_REPOSITORY='adityabhanushali-arch/FullTilt'; $env:GITHUB_REF="refs/tags/v$version"; $env:GITHUB_SHA='local-test-commit'; $env:GH_TOKEN='local-fake-token'
    & "$PSScriptRoot/publish-github-release.ps1" -Tag "v$version" -Assets $testAssets -Publish
    if (($testState.Calls -join ',') -ne 'create,upload,view,edit') { throw 'Draft/upload/verification/publication ordering failed.' }; Write-Host 'PASS: complete draft publishes only after verified upload'
    $testState.Calls.Clear(); $testState.Incomplete=$true
    Expect-Failure { & "$PSScriptRoot/publish-github-release.ps1" -Tag "v$version" -Assets $testAssets -Publish } 'incomplete draft never publishes'
    if ($testState.Calls.Contains('edit')) { throw 'Published an incomplete draft.' }
    $testState.Calls.Clear(); $testState.Incomplete=$false; $testState.FailUpload=$true
    Expect-Failure { & "$PSScriptRoot/publish-github-release.ps1" -Tag "v$version" -Assets $testAssets -Publish } 'upload failure never publishes'
    if ($testState.Calls.Contains('edit')) { throw 'Published after failed upload.' }
}
finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name],'Process') }
    $global:LASTEXITCODE=$savedExitCode
}
Write-Host '6/6 release-script checks passed using scoped fake Git/gh commands. No remote changes.'
