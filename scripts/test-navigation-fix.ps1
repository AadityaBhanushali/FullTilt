# No live registry/file-backup backend is constructed by these tests.
# Both fix scripts embed the same shared code; check it matches, then load it without running either script.
$fixFolder="$PSScriptRoot/../tools/Step-1-Windows-Controller-Fix"
$shared=foreach($name in @('Turn-Off-Windows-Controller-Navigation.ps1','Turn-On-Windows-Controller-Navigation.ps1')){
    $text=[IO.File]::ReadAllText((Join-Path $fixFolder $name))
    $match=[regex]::Match($text,'(?s)# region shared code.*?# endregion shared code')
    if(-not $match.Success){throw "Shared code block missing: $name"}
    $match.Value
}
if($shared[0] -cne $shared[1]){throw 'Turn-Off and Turn-On scripts contain different shared code.'}
. ([scriptblock]::Create($shared[0]))
$cases=@(
    @{Exists=$false;Key=$false;Kind='';Data=$null},
    @{Exists=$false;Key=$true;Kind='';Data=$null},
    @{Exists=$true;Key=$true;Kind='DWord';Data=1},
    @{Exists=$true;Key=$true;Kind='DWord';Data=0},
    @{Exists=$true;Key=$true;Kind='DWord';Data=-1},
    @{Exists=$true;Key=$true;Kind='QWord';Data=[long]9223372036854775807},
    @{Exists=$true;Key=$true;Kind='String';Data='original'},
    @{Exists=$true;Key=$true;Kind='String';Data=([char]0x6F22).ToString()},
    @{Exists=$true;Key=$true;Kind='ExpandString';Data='%SystemRoot%'},
    @{Exists=$true;Key=$true;Kind='Binary';Data=[byte[]]@(0,255,42)},
    @{Exists=$true;Key=$true;Kind='Binary';Data=[byte[]]@()},
    @{Exists=$true;Key=$true;Kind='MultiString';Data=[string[]]@('one','two')}
)
$passed=0
foreach($case in $cases){
    $original=[pscustomobject]@{Version=1;Machine=[Environment]::MachineName;KeyExisted=$case.Key;ValueExisted=$case.Exists;Kind=$case.Kind;Data=$case.Data}
    $state=@{Current=$original;Backup=$null;Writes=0;FailSave=$false;FailRestore=$false}
    $backend=@{
        Read={ $state.Current }.GetNewClosure()
        Load={ $state.Backup }.GetNewClosure()
        Save={param($value) if($state.FailSave){throw 'Fake save failure'}; $state.Backup=($value|ConvertTo-Json -Depth 8|ConvertFrom-Json)}.GetNewClosure()
        Disable={ $state.Writes++;$state.Current=[pscustomobject]@{Version=1;Machine=[Environment]::MachineName;KeyExisted=$true;ValueExisted=$true;Kind='DWord';Data=0} }.GetNewClosure()
        Restore={param($value) if($state.FailRestore){throw 'Fake restore failure'};$state.Writes++;$state.Current=$value}.GetNewClosure()
        Delete={ $state.Backup=$null }.GetNewClosure()
    }
    $state.FailSave=$true;$failed=$false;try{Invoke-NavigationFix Disable $backend}catch{$failed=$true}
    if(-not $failed -or $state.Writes -ne 0){throw 'Save failure must prevent registry writes.'}
    $state.FailSave=$false
    Invoke-NavigationFix Disable $backend
    $first=$state.Backup|ConvertTo-Json -Depth 8 -Compress
    Invoke-NavigationFix Disable $backend
    if(($state.Backup|ConvertTo-Json -Depth 8 -Compress) -cne $first){throw 'Repeat apply replaced original backup.'}
    $state.FailRestore=$true;$failed=$false;try{Invoke-NavigationFix Restore $backend}catch{$failed=$true}
    if(-not $failed -or $null -eq $state.Backup){throw 'Failed restore lost the backup.'}
    $state.FailRestore=$false
    Invoke-NavigationFix Restore $backend
    if(($state.Current|ConvertTo-Json -Depth 8 -Compress) -cne ($original|ConvertTo-Json -Depth 8 -Compress) -or $null -ne $state.Backup){throw 'Exact original state failed round trip.'}
    $failed=$false;try{Invoke-NavigationFix Restore $backend}catch{$failed=$true}
    if(-not $failed){throw 'Missing backup must stop restoration.'}
    $state.Backup=[pscustomobject]@{Version=1;Machine='foreign-machine';KeyExisted=$false;ValueExisted=$false;Kind='';Data=$null}
    $writes=$state.Writes;$failed=$false;try{Invoke-NavigationFix Restore $backend}catch{$failed=$true}
    if(-not $failed -or $state.Writes -ne $writes){throw 'Foreign backup was accepted.'}
    $passed++
}
$files=Get-ChildItem -LiteralPath $fixFolder -File
foreach($file in $files){
    if($file.Extension -eq '.ps1'){$errors=$null;$null=[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$null,[ref]$errors);if($errors.Count){throw "Invalid script syntax: $($file.Name)"}}
    if($file.Extension -eq '.reg'){
        foreach($line in Get-Content -LiteralPath $file.FullName){if($line.Trim() -and -not $line.StartsWith(';') -and $line -ne 'Windows Registry Editor Version 5.00'){throw 'Reference .reg file contains an active operation.'}}
    }
}
Write-Host "PASS: $passed navigation-fix state scenarios, repeat application, preservation/restore failures, missing/foreign backups, syntax and inert registry references. Fake backend only."
