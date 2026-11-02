. "$PSScriptRoot/env.ps1"
$version = Get-FullTiltVersion
$executable = Join-Path $repoRoot "Releases/FullTilt-v$version-win-x64/FullTilt.exe"
$output = Join-Path $repoRoot ('artifacts/package-diagnostics/' + $version + '/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publish FullTilt first.' }
# Only this explicit diagnostic switch is safe for unattended verification.
$process = Start-Process -FilePath $executable -ArgumentList @('--smoke-test', ('"{0}"' -f $output)) -WindowStyle Hidden -PassThru
$completed = $false
# The editor/history/source/drop checks and 192 layouts may use software rendering.
# Keep each wait bounded while allowing the full diagnostic suite to finish.
for ($attempt = 0; $attempt -lt 8; $attempt++) {
    if ($process.WaitForExit(60000)) { $completed = $true; break }
}
if (-not $completed) { throw 'Package diagnostic did not finish within 480 seconds.' }
if ($process.ExitCode -ne 0) {
    foreach ($name in @('smoke-test-error.txt','diagnostic-progress.txt')) {
        $diagnostic=Join-Path $output $name
        if (Test-Path -LiteralPath $diagnostic) { (Get-Content -LiteralPath $diagnostic -Raw).Replace($repoRoot,'<workspace>') | Write-Host }
    }
    throw "Package diagnostic failed with exit code $($process.ExitCode). See $output."
}
$report = Join-Path $output 'smoke-test.txt'
if (-not (Test-Path -LiteralPath $report)) { throw 'Diagnostic report is missing.' }
Get-Content -LiteralPath $report
$expected = ((Get-Content -LiteralPath (Join-Path (Split-Path -Parent $executable) 'SHA256SUMS.txt')) -split ' ')[0]
$actual = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'Package checksum mismatch.' }
Get-Item -LiteralPath $executable | Select-Object FullName, Length
$proof = [ordered]@{ Version=$version; ExecutableSha256=$actual.ToLowerInvariant(); DiagnosticDirectory=$output; VerifiedUtc=[DateTime]::UtcNow.ToString('O') }
$proof | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $executable) 'verification.json') -Encoding utf8
