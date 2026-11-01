# Optional Windows Controller Navigation Fix

Use this only if Windows' own controller navigation in Start, Quick Settings, notifications or the tray conflicts with FullTilt's cursor navigation. FullTilt runs normally without this utility. It never runs automatically.

This utility changes exactly one 64-bit machine-wide Windows registry value:

`HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping`

`Enabled` (DWORD) is set to `0`. It affects Windows' controller-to-virtual-key mapping, not FullTilt's bindings. Administrator privileges are required, and the change affects all users. The creator tested this workaround successfully on their system; Windows builds differ and it is not guaranteed to work everywhere. See the [Windows issue discussion](https://github.com/microsoft/microsoft-ui-xaml/issues/10035).

## Apply

1. Extract the entire folder to a local location. Keep all three PowerShell files together.
2. Open Windows Terminal / PowerShell **as Administrator**. Change directory to this folder.
3. Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Disable_Windows_Controller_UI_Navigation.ps1
```

The execution-policy option applies only to this process; no policy is changed globally. Review the scripts first. They require PowerShell 5.1 or later and do not download anything. Restart Windows if the change does not take effect immediately, then test your controller normally.

Before changing Windows, the script atomically saves the original value, its exact registry type, or its absence in `%ProgramData%\FullTilt-Controller-Navigation-Fix\original-state.json`, restricted to Administrators and SYSTEM. Repeated application never replaces that first backup. If preservation fails, no registry modification is attempted. Do not delete, edit or move this backup to another computer. It contains machine-specific runtime state and must not be shared publicly.

## Restore the previous configuration

From the same elevated PowerShell session:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Restore_Windows_Default_Controller_Navigation.ps1
```

This restores the captured value and type, or removes only the override if it was originally absent. A newly created empty leaf key is removed; unrelated values/subkeys remain. After verified restoration the backup is removed so a future application can capture a new original. A failed restoration retains the backup. If it is missing, invalid, insecurely stored or belongs to another machine, the script stops rather than guessing. Registry access errors are reported; the backup remains available for recovery.

The two original `.reg` files are retained only as commented reference templates. They intentionally perform no registry writes. Use the matching `.ps1` entry points above for backed-up application and accurate restoration. Do not manually re-create an override or delete it unless you understand the original configuration. This utility is covered by FullTilt's MIT License. Automated tests use a fake registry/file backend; the release process never applies this fix.
