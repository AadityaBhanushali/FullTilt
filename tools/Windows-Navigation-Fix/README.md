# Windows Navigation Fix for FullTilt

Windows has its own built-in controller navigation. With a controller connected, it can move focus around Start, Quick Settings, notifications and the tray at the same time as FullTilt moves the cursor, so the two fight each other. This small tool switches the Windows feature off so FullTilt has your controller to itself. You can undo it at any time.

**You only need it if** Start, Quick Settings or notifications react to your controller while FullTilt is running. FullTilt starts fine without it and never runs it for you.

## What it changes

Exactly one 64-bit machine-wide registry value:

`HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping`

`Enabled` (DWORD) is set to `0`. This affects Windows' controller-to-keyboard mapping only, not FullTilt's bindings. Administrator rights are required and the change applies to every user on the PC. It worked on the creator's system, but Windows builds differ, so it isn't guaranteed everywhere. Background: [Windows issue discussion](https://github.com/microsoft/microsoft-ui-xaml/issues/10035).

## Files

| File | Purpose |
| --- | --- |
| `Apply-FullTilt-Navigation-Fix.ps1` | Turns Windows' controller navigation off. |
| `Undo-FullTilt-Navigation-Fix.ps1` | Puts your original setting back. |
| `FullTilt-Navigation-Fix.Common.ps1` | Shared code used by the two scripts above. Keep all three files in the same folder. |

## Apply the fix

1. Extract the whole FullTilt download first, so this folder sits beside `FullTilt.exe`.
2. Open Windows Terminal or PowerShell **as Administrator** and change to this folder, for example:

   ```powershell
   cd "C:\Path\To\FullTilt\Windows-Navigation-Fix"
   ```

3. Run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Apply-FullTilt-Navigation-Fix.ps1
   ```

The execution-policy option applies to this one run only; no policy is changed. Read the scripts first if you like: they need PowerShell 5.1 or later and download nothing. If the change doesn't take effect at once, restart Windows and test your controller.

Before changing anything, the script saves your original value (or the fact that there was none) to `%ProgramData%\FullTilt-Controller-Navigation-Fix\original-state.json`, readable only by Administrators and SYSTEM. Running it again never replaces that first backup, and if saving the backup fails, Windows is left untouched. Keep the backup in place: it is specific to your PC and must not be shared.

## Undo the fix

From the same Administrator PowerShell window:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Undo-FullTilt-Navigation-Fix.ps1
```

This restores the saved value, or removes the override if there was none to begin with. Unrelated registry values stay as they are. After a successful restore the backup is deleted so a future run can capture a fresh original. If restoring fails, or the backup is missing, invalid or from another PC, the script stops without guessing and keeps the backup.

## Notes

This tool is covered by FullTilt's MIT License. Its automated tests use a fake registry and file backend, and FullTilt's release process never applies the fix.
