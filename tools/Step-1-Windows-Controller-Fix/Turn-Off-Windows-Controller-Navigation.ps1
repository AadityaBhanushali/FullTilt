#Requires -Version 5.1
#Requires -RunAsAdministrator
# Turns off Windows' built-in controller navigation so it stops fighting FullTilt.
# Your original setting is saved first. Run Turn-On-Windows-Controller-Navigation.ps1 to undo.
# region shared code (must stay identical in both Turn-Off and Turn-On scripts)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-NavigationSnapshot($State) {
    if ($State.Version -ne 1 -or $State.Machine -cne [Environment]::MachineName -or
        $State.KeyExisted -isnot [bool] -or $State.ValueExisted -isnot [bool]) { throw 'Invalid or foreign-machine backup. No registry change made.' }
    if ($State.ValueExisted) {
        if (-not $State.KeyExisted -or $State.Kind -notin @('DWord','QWord','String','ExpandString','MultiString','Binary','None')) { throw 'Unsupported backup value type.' }
        switch ($State.Kind) {
            DWord { $null = [int]$State.Data }
            QWord { $null = [long]$State.Data }
            { $_ -in 'Binary','None' } { $null = [byte[]]$State.Data }
            MultiString { $null = [string[]]$State.Data }
            default { if ($State.Data -isnot [string]) { throw 'Invalid string backup.' } }
        }
    }
}
function New-NavigationBackend {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Open PowerShell as Administrator, then run this script again.' }
    # Keep this folder name: it holds backups created by FullTilt 1.0.0, which Undo must still find.
    $folder = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'FullTilt-Controller-Navigation-Fix'
    $backup = Join-Path $folder 'original-state.json'
    $admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    if (Test-Path -LiteralPath $folder) {
        if ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Backup directory must not be a link.' }
        if ((Get-Acl -LiteralPath $folder).GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @($admins.Value,$system.Value)) { throw 'Untrusted existing backup directory. No registry change made.' }
    }
    else { $null = New-Item -ItemType Directory -Path $folder }
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($admins); $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @($admins,$system)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    }
    Set-Acl -LiteralPath $folder -AclObject $acl
    $keyPath = 'SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping'
    return @{
        Read = {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64'); $key = $null
            try {
                $key = $base.OpenSubKey($keyPath,$false)
                $exists = $null -ne $key -and $key.GetValueNames() -contains 'Enabled'
                $kind = if ($exists) { $key.GetValueKind('Enabled').ToString() } else { '' }
                $data = if ($exists) { ,($key.GetValue('Enabled',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) } else { $null }
                return [pscustomobject]@{Version=1;Machine=[Environment]::MachineName;KeyExisted=($null -ne $key);ValueExisted=[bool]$exists;Kind=$kind;Data=$data}
            } finally { if ($key) { $key.Dispose() }; $base.Dispose() }
        }.GetNewClosure()
        Disable = {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64'); $key = $null
            try { $key=$base.CreateSubKey($keyPath,$true); $key.SetValue('Enabled',0,[Microsoft.Win32.RegistryValueKind]::DWord) }
            finally { if ($key) { $key.Dispose() }; $base.Dispose() }
        }.GetNewClosure()
        Restore = {
            param($state)
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64'); $key = $null
            try {
                $key=$base.OpenSubKey($keyPath,$true)
                if ($state.ValueExisted) {
                    if (-not $key) { $key=$base.CreateSubKey($keyPath,$true) }
                    $kind=[Microsoft.Win32.RegistryValueKind]$state.Kind
                    $data=switch ($state.Kind) { DWord { [int]$state.Data }; QWord { [long]$state.Data }; Binary { ,([byte[]]$state.Data) }; None { ,([byte[]]$state.Data) }; MultiString { ,([string[]]$state.Data) }; default { [string]$state.Data } }
                    $key.SetValue('Enabled',$data,$kind)
                }
                elseif ($key) { $key.DeleteValue('Enabled',$false) }
                if (-not $state.KeyExisted -and $key -and $key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) {
                    $key.Dispose(); $key=$null; $base.DeleteSubKey($keyPath,$false)
                }
            } finally { if ($key) { $key.Dispose() }; $base.Dispose() }
        }.GetNewClosure()
        Load = {
            if (-not (Test-Path -LiteralPath $backup)) { return $null }
            if ((Get-Item -LiteralPath $backup -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Backup file must not be a link.' }
            $fileAcl=Get-Acl -LiteralPath $backup
            if ($fileAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @($admins.Value,$system.Value)) { throw 'Untrusted backup owner.' }
            foreach ($rule in $fileAcl.Access) {
                $sid=$rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
                if ($rule.AccessControlType -eq 'Allow' -and $sid -notin @($admins.Value,$system.Value)) { throw 'Backup permissions are not private to administrators and SYSTEM.' }
            }
            return ([IO.File]::ReadAllText($backup) | ConvertFrom-Json)
        }.GetNewClosure()
        Save = {
            param($state)
            $temporary=Join-Path $folder ([Guid]::NewGuid().ToString('N')+'.tmp')
            try {
                [IO.File]::WriteAllText($temporary,($state | ConvertTo-Json -Depth 8))
                $fileAcl=Get-Acl -LiteralPath $temporary; $fileAcl.SetOwner($admins); Set-Acl -LiteralPath $temporary -AclObject $fileAcl
                [IO.File]::Move($temporary,$backup) # No overwrite: preserve the first original.
            } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
        }.GetNewClosure()
        Delete = { Remove-Item -LiteralPath $backup -Force }.GetNewClosure()
    }
}
function Invoke-NavigationFix {
    param([ValidateSet('Disable','Restore')][string]$Mode, [hashtable]$Backend)
    if (-not $Backend) { $Backend=New-NavigationBackend }
    $state=& $Backend.Load
    if ($Mode -eq 'Disable') {
        if ($null -eq $state) { $state=& $Backend.Read; Assert-NavigationSnapshot $state; & $Backend.Save $state }
        else { Assert-NavigationSnapshot $state }
        & $Backend.Disable
        $current=& $Backend.Read
        if (-not $current.ValueExisted -or $current.Kind -ne 'DWord' -or $current.Data -ne 0) { throw 'Windows did not retain Enabled=0. Original backup retained.' }
        Write-Host 'Done: Windows controller navigation is off. Your original setting is saved. Restart Windows if it still reacts to the controller.'
    }
    else {
        if ($null -eq $state) { throw 'No saved original setting found, so nothing was changed. Run Turn-Off-Windows-Controller-Navigation.ps1 first.' }
        Assert-NavigationSnapshot $state
        & $Backend.Restore $state
        $current=& $Backend.Read
        if ($current.ValueExisted -ne $state.ValueExisted -or ($state.ValueExisted -and
            ($current.Kind -ne $state.Kind -or ($current.Data | ConvertTo-Json -Compress) -cne ($state.Data | ConvertTo-Json -Compress)))) { throw 'Restoration verification failed. Backup retained.' }
        & $Backend.Delete
        Write-Host 'Done: your original Windows controller navigation setting is back. Restart Windows if needed.'
    }
}
# endregion shared code
Invoke-NavigationFix -Mode Disable
