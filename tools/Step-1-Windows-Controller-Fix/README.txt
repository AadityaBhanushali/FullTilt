STEP 1: WINDOWS CONTROLLER FIX
Do this once, before you open FullTilt.


THE PROBLEM
-----------
Windows has its own controller navigation built in. When a controller is
connected, Windows uses it to move around Start, Quick Settings,
notifications and the taskbar tray. FullTilt uses the same controller to
move your mouse. Both react at the same time and get in each other's way.


THE FIX
-------
This folder has two scripts:

  Turn-Off-Windows-Controller-Navigation.ps1
      Turns Windows' controller navigation OFF, so only FullTilt responds.
      This is the one to run.

  Turn-On-Windows-Controller-Navigation.ps1
      Turns it back ON, exactly as it was before.


HOW TO RUN IT
-------------
1. Open the Start menu, type PowerShell, right-click it and choose
   "Run as administrator".

2. Go to this folder. Change the path to wherever you extracted FullTilt:

     cd "C:\Path\To\FullTilt\Step-1-Windows-Controller-Fix"

3. Run:

     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Turn-Off-Windows-Controller-Navigation.ps1

4. You should see "Done". If Windows still reacts to the controller,
   restart your PC.

To undo it later, do the same steps with
Turn-On-Windows-Controller-Navigation.ps1.


GOOD TO KNOW
------------
- It's safe to undo. Before changing anything, the script saves your
  original setting, and turning it back on restores exactly that.

- It changes one Windows setting: the "Enabled" value under
  HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping
  It affects all users on this PC. Nothing else is changed and nothing is
  downloaded.

- Windows only lets administrators change that setting, which is why you
  need "Run as administrator".

- "-ExecutionPolicy Bypass" only applies to this one run. It doesn't
  change your PC's PowerShell settings.

- The saved original setting is kept in
  %ProgramData%\FullTilt-Controller-Navigation-Fix\original-state.json
  Only administrators can read it. Leave it there; it is removed
  automatically when you turn navigation back on.

- FullTilt never runs these scripts by itself.

- This worked on the creator's PC, but Windows versions differ.
  Background: https://github.com/microsoft/microsoft-ui-xaml/issues/10035
