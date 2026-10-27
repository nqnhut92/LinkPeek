# Windows changes

## 0.1.1 — in development

This Windows version is not final. Iteration builds keep version 0.1.1; use the source commit in `BUILD-INFO.txt` to distinguish them. No new release tag is implied by these changes.

### Current iteration

- Refresh the hover popup and Settings with a Windows 11 style: lighter visual hierarchy, rounded accent actions, sidebar navigation, and light/dark palettes. Use native rounded outer-window corners on Windows 11 and high-contrast system colors when enabled.
- Scope hyperlink detection to the foreground native root window rather than requiring UIA renderer elements to share the host process ID. This supports hosted-renderer layouts used by desktop apps such as Teams; actual Teams desktop behavior still needs user retesting.
- Prioritize hyperlink ancestors of hit text nodes and inspect MSAA ancestors instead of only the exact hit child.
- Add an explicit delayed **Diagnose hovered link…** action that reports accessibility structure without URL values, labels, messages or credentials.
- Add native cross-process accessibility regression checks and light/dark synthetic UI previews to Windows CI.
- Keep Windows at version 0.1.1 while the port is finalized. macOS source, tags and assets remain unchanged.

### Initial implementation

- Initial portable Windows x64/ARM64 port: tray app, offline QR, Azure DevOps preview/verified approval, Windows Credential Manager, optional sign-in startup, and manual URL entry.
- User reported working hover in other applications and Teams web; desktop Teams did not resolve full URL links.
