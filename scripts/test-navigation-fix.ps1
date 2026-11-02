# Checks the two double-click registry files in the Step 1 folder. Reads files only; never imports them.
$fixFolder = "$PSScriptRoot/../tools/Step-1-Windows-Controller-Fix"
$key = '[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping]'
$expected = @{
    'Disable_Windows_Controller_UI_Navigation.reg'      = '"Enabled"=dword:00000000'
    'Restore_Windows_Default_Controller_Navigation.reg' = '"Enabled"=-'
}
foreach ($name in $expected.Keys) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $fixFolder $name))
    if ($bytes.Length -lt 2 -or $bytes[0] -ne 0xFF -or $bytes[1] -ne 0xFE) { throw "$name must stay UTF-16 with a byte-order mark so Registry Editor accepts it." }
    $lines = @([Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2) -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith(';') })
    $want = @('Windows Registry Editor Version 5.00', $key, $expected[$name])
    if (@(Compare-Object $want $lines -SyncWindow 0).Count -ne 0) { throw "$name changes something other than the controller navigation value." }
}
$files = @(Get-ChildItem -LiteralPath $fixFolder -File | ForEach-Object Name | Sort-Object)
if (($files -join ',') -ne 'Disable_Windows_Controller_UI_Navigation.reg,README.txt,Restore_Windows_Default_Controller_Navigation.reg') { throw "Step 1 folder must hold exactly the two registry files and README.txt." }
Write-Host 'PASS: Step 1 registry files set or remove only the Windows controller navigation value; folder holds exactly three files.'
