# Windows changes

## 0.1.1 — in development

This Windows version is not final. Iteration builds keep version 0.1.1; use the source commit in `BUILD-INFO.txt` to distinguish them. No new release tag is implied by these changes.

### Current iteration

- Fix the tray app quitting after running for a while. WPF ends the process on an unhandled UI exception, where the previous WinForms loop showed a dialog. UI-thread and tray-icon exceptions are now logged to `%LOCALAPPDATA%\LinkPeek\crash.log` and handled; LinkPeek closes the current card, shows one notification and keeps running. The theme is re-applied only when Windows' theme or accent actually changes, instead of reloading theme resources on every hover.
- CI opens and closes 200 hover cards and fails on leaked windows, GDI/USER handles or per-hover theme reloads.
- Rebuild the Windows interface on WPF and WPF UI 4.3.0 (Fluent Design) instead of custom-drawn WinForms controls: Mica Settings window with `NavigationView` and settings cards, Fluent toggle switches, slider, text and password inputs, `InfoBar` status messages, Fluent System Icons, a Fluent tray context menu, and ContentDialog-style dialogs for Open link, diagnostics, approval and PAT removal.
- Redesign the hover card as a Fluent flyout with a vector QR code, icon detail rows and inline warnings/errors. Positioning, focus behavior (hover cards never activate), dwell, suppression and the approval flow are unchanged.
- Settings apply immediately; the organization is saved when leaving its field. Removing a PAT now asks for confirmation.
- Show the saved-PAT state like the macOS app: eight fixed placeholder-colored dots when a credential exists and the input is empty. Only credential existence is checked; the token is never read for display, its length is not revealed and the dots are not inserted into the input.
- Follow Windows light/dark/high-contrast and accent changes while windows are open. Declare per-monitor V2 DPI awareness for the WPF UI.
- CI renders light/dark previews of every window at 100%, 150% and 200%, fails on clipped text or controls outside their window, and verifies saved-PAT mask alignment, tone and behavior.
- Hover detection (UI Automation/MSAA), QR generation and Azure DevOps logic are unchanged. Users confirmed hover works across apps, including Teams desktop.

#### Earlier in this iteration

- Refresh the hover popup and Settings with a Windows 11 style: lighter visual hierarchy, rounded accent actions, sidebar navigation, and light/dark palettes. Use native rounded outer-window corners on Windows 11 and high-contrast system colors when enabled.
- Scope hyperlink detection to the foreground native root window rather than requiring UIA renderer elements to share the host process ID. This supports hosted-renderer layouts used by desktop apps such as Teams; actual Teams desktop behavior still needs user retesting.
- Prioritize hyperlink ancestors of hit text nodes and inspect MSAA ancestors instead of only the exact hit child.
- Add an explicit delayed **Diagnose hovered link…** action that reports accessibility structure without URL values, labels, messages or credentials.
- Add native cross-process accessibility regression checks and light/dark synthetic UI previews to Windows CI.
- Keep Windows at version 0.1.1 while the port is finalized. macOS source, tags and assets remain unchanged.

### Initial implementation

- Initial portable Windows x64/ARM64 port: tray app, offline QR, Azure DevOps preview/verified approval, Windows Credential Manager, optional sign-in startup, and manual URL entry.
- User reported working hover in other applications and Teams web; desktop Teams did not resolve full URL links.
