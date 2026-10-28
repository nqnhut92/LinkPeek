# Windows changes

## 0.2.0

Matches the macOS 0.2.0 hover changes.

- New **QR links** page: a master switch, a built-in Grab links preset (`app.grab.com/s/*`), your own case-insensitive `*` patterns and a link tester. Other links no longer open a QR card unless **Show QR for any other link** is on.
- The default hover delay is 800 ms and the slider covers 500–1500 ms. A one-time migration moves the old 500 ms default to 800 ms. Hover timing stays on the General page.
- **Azure DevOps → Show PR card on hover** turns pull request cards off; those links then follow the QR rules.

## 0.1.1

First Windows release of LinkPeek. The macOS app, its tags and its assets are unchanged. Use the source commit in `BUILD-INFO.txt` to tell builds apart.

- Tray utility for Windows 10 22H2 / Windows 11, x64 and ARM64, as a portable self-contained package that needs no installer, administrator rights or .NET runtime.
- Hover a web link to get an offline QR code, or an Azure DevOps pull request preview with verified approval. Detection uses UI Automation and MSAA, including hyperlinks exposed by a separate renderer process inside the foreground window (such as Teams desktop). **Open link…** accepts a pasted URL when an app does not expose its link.
- Azure DevOps PATs live in Windows Credential Manager per organization. When one is saved and the input is empty, Settings shows eight fixed placeholder dots, like the macOS app; the token is never read or shown for this.
- Fluent Design interface built on WPF and WPF UI 4.3.0: Mica Settings window with navigation and settings cards, Fluent popover hover card with an arrow pointing at the link and a vector QR code, dialogs and tray menu, light/dark/high-contrast themes with the Windows accent color, per-monitor DPI. Settings apply immediately.
- Unexpected errors are logged to `%LOCALAPPDATA%\LinkPeek\crash.log` and do not close the tray app.
- CI runs the core tests, a cross-process hyperlink fixture, UI layout and saved-PAT checks and a hover-card lifecycle test, and builds both packages.
