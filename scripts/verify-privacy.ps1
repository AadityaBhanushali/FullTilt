param([Parameter(Mandatory)][string]$Path, [switch]$ManagedAssemblies, [switch]$VerifyRuntimeNotices)
. "$PSScriptRoot/env.ps1"
$files = if (Test-Path -LiteralPath $Path -PathType Container) { @(Get-ChildItem -LiteralPath $Path -File -Recurse) } else { @(Get-Item -LiteralPath $Path) }
$pattern = '(?i)[a-z]:[\\/]+Users[\\/]|/[U]sers/|/[h]ome/|[A-Z0-9._%+-]+@(gmail|yahoo|hotmail|outlook)\.com|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16}|BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY'
foreach ($file in $files) {
    $filePattern = $pattern
    if ($VerifyRuntimeNotices -and $file.Name -eq 'THIRD-PARTY-NOTICES.TXT' -and $file.FullName -match '[\\/]runtime-notices[\\/]') {
        # Public upstream copyright contacts must remain in their licensed notices.
        # Permit those emails only after byte-for-byte comparison with the restored pack.
        $packName = $file.Directory.Name
        $version = Get-FullTiltVersion
        $packs = Get-Content -LiteralPath (Join-Path $repoRoot "Finals/FullTilt-v$version-win-x64/runtime-packs.json") -Raw | ConvertFrom-Json
        $pack = @($packs | Where-Object Name -eq $packName)
        if ($pack.Count -ne 1) { throw 'Unrecognized runtime notice source.' }
        $original = Join-Path $env:NUGET_PACKAGES ($packName + '/' + $pack[0].Version + '/THIRD-PARTY-NOTICES.TXT')
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash) { throw 'Runtime notices were modified.' }
        $filePattern = $filePattern.Replace('[A-Z0-9._%+-]+@(gmail|yahoo|hotmail|outlook)\.com|','')
    }
    $stream = [IO.File]::OpenRead($file.FullName)
    try {
        $buffer = [byte[]]::new(1048576); $tail = [byte[]]::new(0)
        while (($count = $stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            $chunk = [byte[]]::new($tail.Length+$count)
            [Array]::Copy($tail,0,$chunk,0,$tail.Length); [Array]::Copy($buffer,0,$chunk,$tail.Length,$count)
            foreach ($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
                if ($encoding.GetString($chunk) -match $filePattern) { throw "Privacy pattern detected in $($file.Name). Matched content is intentionally not printed." }
            }
            $keep = [Math]::Min(512,$chunk.Length); $tail=[byte[]]::new($keep); [Array]::Copy($chunk,$chunk.Length-$keep,$tail,0,$keep)
        }
    }
    finally { $stream.Dispose() }
    if ($ManagedAssemblies -and $file.Name -like 'FullTilt*.dll') {
        $stream=[IO.File]::OpenRead($file.FullName); $pe=[System.Reflection.PortableExecutable.PEReader]::new($stream)
        try { foreach ($entry in $pe.ReadDebugDirectory()) { if ($entry.Type -in @([System.Reflection.PortableExecutable.DebugDirectoryEntryType]::EmbeddedPortablePdb,[System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView)) { throw "Production assembly contains debug symbols: $($file.Name)" } } }
        finally { $pe.Dispose(); $stream.Dispose() }
    }
}
Write-Host "PASS: privacy scan of $($files.Count) file(s); no private user paths or credential patterns; personal-provider emails rejected except byte-verified upstream copyright notices."
