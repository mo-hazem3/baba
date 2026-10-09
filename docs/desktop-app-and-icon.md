# Baba as an app on the computer, with its own icon

Baba is meant to be used by people who never see code: they install it once, then double-click the **Baba** icon on the desktop. This
page says how to make that installer, what the people who install it will see, and how to put your own icon on it.

## 1. Make the installer (done by the person who builds Baba, not by users)

1. Open PowerShell in the Baba folder (`c:\dev\baba`).
2. Run: `powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1`
3. After a few minutes the installer is at `artifacts\installer\Baba-Setup-<version>.exe` (about 50 MB, everything inside; the person who
   installs it does not need .NET or anything else except the Microsoft Edge WebView2 runtime, which Windows 10 and 11 already have).

The version number is in `Directory.Build.props`. Build a fresh installer whenever the app changed. (Last built and tested on 2026-10-09, now with the Baba icon and the Phase 7 screens:
silent install, desktop shortcut, `.baba` file association, the app's own self-test and every screen flow from the installed copy, then
uninstall leaving nothing behind.)

## 2. What the person who installs it does

1. Double-click `Baba-Setup-<version>.exe` (copy it to them on a USB stick or by e-mail/cloud link).
2. Click **Next**. The box **Create a desktop shortcut** is ticked already. Click **Install**, then **Finish**.
3. From then on: double-click **Baba** on the desktop (or find it in the Start menu). A company file (`.baba`) also opens by double-click
   in Explorer, and it opens in Baba.

It installs for the current Windows user only (no administrator password needed). To remove it: Windows Settings > Apps > Baba > Uninstall;
company files are never touched.

## 3. Your own icon

**Status (2026-10-09): the icon has been provided and is in `branding\` (`baba.ico`, `baba.png`, `baba.svg`); the program, the installer and the web header already use it. Rebuild the installer (section 1) to ship it.** To change the icon later, replace those files as described below.


1. Make the picture: a **square** logo, at least 256 x 256 pixels, ideally 1024 x 1024, transparent background (PNG).
2. Turn it into a Windows icon file named **`baba.ico`** that contains several sizes (16, 24, 32, 48, 64, 128 and 256 pixels). Free web
   converters ("PNG to ICO", choose all sizes) or tools such as IcoFX or GIMP do this. Or give Claude the PNG and ask for the `.ico`.
3. Put the file at **`c:\dev\baba\branding\baba.ico`** (make the `branding` folder). Nothing else has to be edited: the project picks the
   file up by itself if it exists.
4. Build the installer again (section 1). The icon is then used for: the program file, the window and taskbar button, the desktop and
   Start-menu shortcuts, the installer itself, and the icon of `.baba` files in Explorer.
5. If the icon looks old on the desktop after reinstalling, restart Explorer or sign out and in (Windows caches icons).

How it is wired: `src/Baba.Desktop/Baba.Desktop.csproj` (`ApplicationIcon`, only when the file exists), `MainForm` (window icon taken from the
program file), `installer/Baba.iss` (`SetupIconFile`, only when the file exists). Without `branding\baba.ico` everything works with Windows'
default icon.

The **name** shown under the icon is "Baba". An Arabic or different product name is changed in `installer/Baba.iss` (`AppName`, the two
`[Icons]` lines) and in the window title (`MainForm`); decide the name first, then rebuild.

## 4. Things to know before giving the installer to real customers (honest list)

- **"Windows protected your PC" warning.** An installer that is not code-signed shows Microsoft SmartScreen's blue warning; users have to
  click "More info" then "Run anyway". A real customer release needs a code-signing certificate (bought from a certificate authority; the
  installer is then signed with it). Not done yet.
- **WebView2.** If a computer does not have the Microsoft Edge WebView2 runtime the installer opens Microsoft's download page and asks to run
  setup again. For non-technical users it is better to bundle Microsoft's small bootstrapper into the installer. Not done yet; today's
  Windows 10 and 11 already include it.
- **Updates.** There is no automatic update yet: a new version is a new installer run over the old one (company files are not touched;
  the app makes a backup before changing a file's structure).
- **Testing on a clean computer.** The installer has been tested on the development computer only (install, run, uninstall). Try it once
  on a second Windows computer without any developer tools before the first customer.
- The Windows "double-click the Save dialog" steps (new-company file dialog, backup and restore dialogs, window-close warning) were not
  driven automatically in the last checks; try them by hand on the installed app.
