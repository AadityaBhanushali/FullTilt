# Changelog

Public releases use semantic versions matching the application metadata and `vMAJOR.MINOR.PATCH` tags. Earlier 4.x labels were development builds, not public releases.

## [1.0.2] - 2026-11-01

The Windows controller fix is back to two simple double-click files. The app itself is unchanged.

- `Step-1-Windows-Controller-Fix` now holds the original `Disable_Windows_Controller_UI_Navigation.reg` and `Restore_Windows_Default_Controller_Navigation.reg`, plus a short `README.txt`. No PowerShell or commands needed.
- If you used the PowerShell fix from 1.0.0 or 1.0.1, the Restore file still returns Windows to its default.

### How to install

1. Download **FullTilt-win-x64.zip** below and extract it (right-click → Extract All).
2. **Step 1:** in `Step-1-Windows-Controller-Fix`, double-click `Disable_Windows_Controller_UI_Navigation.reg` and choose Yes. Undo any time with `Restore_Windows_Default_Controller_Navigation.reg`.
3. **Step 2:** open **FullTilt.exe**.

You can also try FullTilt before Step 1; the fix just makes it work more smoothly alongside Windows.

**Updating:** exit FullTilt, then extract the new files over your old folder. Your `Profiles` folder and settings are kept.

## [1.0.1] - 2026-11-01

A simpler download. The app itself is unchanged.

- The ZIP now contains only `FullTilt.exe`, the `Step-1-Windows-Controller-Fix` folder and a `Licenses` folder. Guides, changelog and screenshots live on GitHub instead.
- The Windows controller fix is now a clear first step, with two scripts that say what they do: `Turn-Off-Windows-Controller-Navigation.ps1` and `Turn-On-Windows-Controller-Navigation.ps1`, plus one plain-text `README.txt`.
- If you used the fix from 1.0.0, the new Turn-On script can still restore your original setting.

### How to install

1. Download **FullTilt-win-x64.zip** below and extract it (right-click → Extract All).
2. **Step 1, once:** open PowerShell **as administrator**, go to the `Step-1-Windows-Controller-Fix` folder and run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Turn-Off-Windows-Controller-Navigation.ps1`. This stops Windows' own controller navigation from fighting FullTilt. Full steps are in the folder's `README.txt`.
3. Connect your controller and open **FullTilt.exe**.

**Updating from 1.0.0:** exit FullTilt, then extract the new files over your old folder. Your `Profiles` folder and settings are kept. You can delete the old `FullTilt-Controller-Navigation-Fix`, `docs` and `runtime-notices` folders and the loose `.md` files.

## [1.0.0] - 2026-11-01

First public release of FullTilt, a free, open-source controller alternative to the mouse and keyboard for Windows 11 x64.

- Move the cursor and scroll with an XInput controller.
- Click, drag, use keyboard shortcuts and configure individual controller bindings.
- Adjust independent normal, precision and boosted cursor speeds, scrolling and response settings.
- Open directional pie menus with configurable segments and contextual D-pad actions.
- Use Windows navigation, window, editing and media controls.
- Save profiles, retain portable settings, and undo or discard assignment drafts.
- Use the Dark, Light or system theme, tray controls, and configurable pause shortcut.
- Check GitHub for stable updates manually or automatically; installation remains manual.
- Includes the optional FullTilt-Controller-Navigation-Fix utility for conflicting Windows controller navigation, with administrator-only original-state backup and restoration. FullTilt never applies it automatically.

### Install or update

Download **FullTilt-win-x64.zip**, extract it to a writable folder and run **FullTilt.exe**. The Windows x64 .NET runtime is included. Connect an XInput controller, center the sticks and release its buttons before input arms. Close the app through **File → Exit FullTilt** or tray **Exit** before replacing files. Keep your existing **Profiles** folder; the ZIP contains no personal settings.

Development builds labeled 4.8 use numeric version 1.4.8, which compares newer than public 1.0.0. Existing development users must manually install this first public release and copy their Profiles folder before first launch. Public 1.0.0 will then detect future public releases such as 1.1.0 normally. Profile schema and existing settings meanings remain unchanged.
