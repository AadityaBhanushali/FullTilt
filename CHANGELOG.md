# Changelog

Public releases use semantic versions matching the application metadata and `vMAJOR.MINOR.PATCH` tags. Earlier 4.x labels were development builds, not public releases.

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

### Install or update

Download **FullTilt-win-x64.zip**, extract it to a writable folder and run **FullTilt.exe**. The Windows x64 .NET runtime is included. Connect an XInput controller, center the sticks and release its buttons before input arms. Close the app through **File → Exit FullTilt** or tray **Exit** before replacing files. Keep your existing **Profiles** folder; the ZIP contains no personal settings.

Development builds labeled 4.8 use numeric version 1.4.8, which compares newer than public 1.0.0. Existing development users must manually install this first public release and copy their Profiles folder before first launch. Public 1.0.0 will then detect future public releases such as 1.1.0 normally. Profile schema and existing settings meanings remain unchanged.
