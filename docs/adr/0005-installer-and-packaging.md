# ADR 0005: Installer (Inno Setup), self-contained app, `.baba` file association

- Status: accepted
- Date: 2026-10-06

## Context

Each phase must end with an installable app (brief section 13), and double-clicking a `.baba` file must open it
(brief sections 1 and 2). The brief offers MSIX or Inno Setup.

## Decision

- **Inno Setup 6** builds `Baba-Setup-<version>.exe` from `installer/Baba.iss`. MSIX needs a signing certificate to
  install at all, while Inno Setup works unsigned (Windows SmartScreen will warn until the installer is code-signed;
  buying a certificate is a business decision for later).
- **Self-contained publish** (`dotnet publish -r win-x64 --self-contained`), so users need nothing installed except the
  WebView2 runtime, which ships with current Windows 10 and 11. The cost is size (about 157 MB published, 49 MB installer).
- **Per-user install by default** (no administrator rights), with a dialog to install for all users.
- **File association** under `HKA\Software\Classes`: `.baba` opens with `Baba.Desktop.exe "%1"`. A second launch hands the
  path to the running window (ADR 0003). Uninstall removes both registry keys.
- **WebView2 check** in the installer (offers the Microsoft download page) and in the app itself (a bilingual message).
- **One version** in `Directory.Build.props` feeds every assembly and the installer. `installer/build-installer.ps1`
  builds the web app, publishes, and compiles the installer; without Inno Setup it makes a portable zip instead.

## Verified

The built installer was run silently: it installs, registers the association, the installed app passes the desktop
smoke test (window, API, web app, three PDFs), and uninstalling removes the files and both registry keys.

## Consequences

- No application icon yet (Windows shows a default one). A real icon belongs with branding and the Arabic product name
  decision (brief section 15).
- The installer text is English only for now.
- Updates are a new installer run over the old one (same `AppId`); company files are never touched by install or uninstall.
- Not covered by automatic tests: the installer itself (checked by hand and by the commands above), the missing-WebView2
  message, and the close-window confirmation dialog.
