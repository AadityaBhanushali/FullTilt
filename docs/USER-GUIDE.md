# FullTilt user guide

Everything you need after the [quick start in the README](../README.md#get-started): the four pages of the app, how pie menus work, where your settings live, how to update, and what to do when something misbehaves.

## Contents

- [The four pages](#the-four-pages)
- [Using pie menus](#using-pie-menus)
- [Pausing and exiting](#pausing-and-exiting)
- [Profiles and settings](#profiles-and-settings)
- [Updating and rolling back](#updating-and-rolling-back)
- [Default setup](#default-setup)
- [Troubleshooting](#troubleshooting)
- [Step 1: Windows controller fix](#step-1-windows-controller-fix)

## The four pages

| Page | What it does | Saving |
| --- | --- | --- |
| **Keybindings** | Assign an action to every controller button and D-pad direction. Pick a button on the controller or in the list, then search the action library or use **Record key / shortcut** to capture a real key combination. | Edits are a draft until **Save changes**. Undo, Redo and Discard work per page. |
| **Movement** | Cursor stick, normal / precision / boosted cursor speed, scroll speed, reverse scrolling, response curve, dead zones and D-pad movement. | Applies and saves immediately. Undo, Redo and **Reset movement** are available. |
| **Pie Menus** | Create, rename, clone and delete radial menus, choose how each opens, set 2 to 8 segments, fill them and set D-pad overrides. | Draft until **Save changes**. |
| **Settings** | Start with Windows, theme (System / Light / Dark), on-screen state indicator, controller selection, pause shortcut, profile folder. | Saves immediately. |

All numbers are whole numbers. Switching profiles or closing the app while you have unsaved Keybindings or Pie Menus edits asks whether to **Save**, **Discard** or **Cancel**.

## Using pie menus

1. **Hold** the button that opens the menu (LB, RB, RT, L3 or R3 by default).
2. **Point** the cursor stick toward a segment.
3. **Release** the button to run the highlighted action.

To cancel, bring the stick back to the center for a moment before releasing. While a menu is open, its **D-pad** directions run the actions set for that menu in the editor; directions without an override use your Keybindings. Menu size is one global setting that applies to every desktop pie menu and every profile.

A new pie menu starts with four empty segments and no opening button. Assign a button in **Activate with** once it is ready.

## Pausing and exiting

- **Pause / resume**: press the pause shortcut (default **D-pad Up + B + Y** together), or use **Settings → Controller → Pause / Resume**. You can change the shortcut in Settings.
- **Release held inputs** lets go of any key, mouse button or mode FullTilt is holding. FullTilt also releases everything and waits for the sticks and buttons to return to neutral after a pause or when a controller disconnects.
- Closing the window keeps FullTilt running in the tray. Choose **File → Exit FullTilt**, or **Exit** in the tray menu, to quit completely.
- Run `FullTilt.exe --paused` to start with controller output paused.

## Profiles and settings

Your profiles, keybindings, pie menus and movement settings are stored in **`Profiles/settings.json`** next to `FullTilt.exe`. Theme, window mode and update preferences are in **`Profiles/app-preferences.json`**. To move FullTilt to another folder or PC, copy the whole `Profiles` folder along with it.

- Use the profile bar at the top of the window to Save, Rename, Delete or create a **New** profile.
- **Start with Windows** is on for new installs and belongs to your Windows account. Turn it off in **Settings → General**. If you move the executable, switch it off and on again from the new location.
- Never run two copies of FullTilt against the same `Profiles` folder.

## Updating and rolling back

FullTilt checks GitHub's latest stable release on startup and roughly every six hours (you can switch this off in **File → Check for Updates**). It tells you about new versions but never downloads or installs them.

To update:

1. Exit FullTilt (**File → Exit FullTilt**).
2. Back up your current folder.
3. Download the new `FullTilt-win-x64.zip` and extract it **over** your current folder, or extract it to a new folder and copy your `Profiles` folder in **before** first launch. Release ZIPs contain no personal settings, so your `Profiles` folder is kept.

To roll back, run the old folder you kept, along with the backup of its `Profiles` folder.

## Default setup

A fresh install, and **Reset current profile**, use this configuration.

| Control | Default behavior |
| --- | --- |
| Left stick / right stick | Cursor / scrolling |
| A / X / Y | Left / right / middle mouse button, held while pressed |
| B | Escape |
| D-pad | Arrow keys |
| LT | Speed Boost while held |
| LB / RB | Media / Editing pie menu |
| L3 / R3 | Windows pie menu |
| RT | Keyboard pie menu |
| D-pad Up + B + Y | Pause or resume |

Movement defaults: normal cursor **600 px/s**, precision **200 px/s**, boosted **3000 px/s**, scrolling **10** steps/s (**40** boosted), dead zones **5%**, response **260%**, pie menu size **120%**. Five pie menus are included (Media, Browsing, Windows, Editing, Keyboard); Browsing is not bound to a button until you choose one.

## Troubleshooting

**Nothing happens when I move the stick.** Check that FullTilt isn't paused (the on-screen indicator and **Settings → Controller** show this), that the controller shows as connected, and that the sticks were centered when it started. Controllers that don't speak XInput need an external mapping tool.

**Some apps ignore FullTilt.** FullTilt sends normal Windows input. Windows blocks input from a standard process into an elevated (administrator) window, and some games read hardware directly. Run FullTilt as administrator only if you understand the trade-offs.

**Windows' own controller navigation fights with FullTilt in Start, Quick Settings or the tray.** Run the [Step 1 fix](#step-1-windows-controller-fix).

**Windows warns about the app.** The executable isn't code-signed, so SmartScreen may show a reputation prompt. Choose **More info → Run anyway**, or compare the ZIP with `SHA256SUMS.txt` on the release page first.

**The app won't start.** The bundled .NET runtime unpacks native files into your user temp folder, which must be writable. Extract FullTilt to a normal folder rather than running it from inside the ZIP.

**A key or button seems stuck.** Use **Release held inputs** in Settings, or pause and resume.

Still stuck? [Open an issue](https://github.com/AadityaBhanushali/FullTilt/issues/new/choose) with your FullTilt and Windows versions, controller model and steps to reproduce. Remove personal details from screenshots and logs.

## Step 1: Windows controller fix

Windows has its own controller navigation for Start, Quick Settings and notifications. It reacts to the same controller FullTilt uses, so the two get in each other's way.

In the download, open `Step-1-Windows-Controller-Fix`:

- Double-click **`Disable_Windows_Controller_UI_Navigation.reg`** and choose **Yes** to turn Windows' controller navigation off.
- Double-click **`Restore_Windows_Default_Controller_Navigation.reg`** and choose **Yes** to put Windows back to its default.

Each file changes only the `Enabled` value under `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping`, for all users on the PC. FullTilt never changes this setting by itself. Results can vary between Windows builds.

## Limits worth knowing

FullTilt's automated tests use fake controllers and isolated settings. Physical controller behavior, focus handling in other applications, GPU blur effects and multi-monitor setups depend on your hardware and software, and are not covered by automated checks.
