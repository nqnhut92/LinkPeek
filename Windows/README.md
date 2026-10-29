# LinkPeek for Windows

Native Windows port of LinkPeek: a tray utility that previews web links as offline QR codes or Azure DevOps pull requests. The existing Swift/macOS application remains in the repository root; Windows code lives here.

Targets Windows 10 22H2 / Windows 11, x64 or ARM64. Development requires the .NET 10 SDK. The published portable app includes its runtime and runs as the current user, without administrator privileges or a separate .NET installation. Hover detection has been confirmed to work across the apps its author uses, including Teams desktop; see [Detection across applications](#detection-across-applications) for what depends on each app. See the [acceptance checklist](#windows-acceptance-checklist) and [Windows changes](CHANGELOG.md).

## Interface

The Windows UI is built with WPF and [WPF UI](https://github.com/lepoco/wpfui) 4.3.0, a Fluent Design toolkit that implements WinUI controls for WPF. It follows the patterns of Windows 11 Settings, PowerToys Settings and WinUI Gallery rather than drawing custom controls:

- **Settings** is a Mica window with a Fluent title bar and a left `NavigationView` (General, Azure DevOps, About). Each option is a settings card (`CardControl`) with an icon, title, description and a Fluent `ToggleSwitch`, `Slider`, `TextBox` or `PasswordBox`. Changes apply immediately, like Windows Settings; there is no separate Save button. The organization is saved when you leave its field. Status and errors appear in an `InfoBar` at the top of the page.
- **Hover card** is a compact popover like the macOS one: a single rounded Fluent surface with an arrow that points at the hovered link, a vector QR code that stays sharp at any scale, an `InfoBar` for warnings and errors, and accent/standard Fluent buttons. It sits below the link, flips above when there is no room, then goes beside the link (or beside the pointer for a very wide link) so it never floats free of it; the arrow points at the link from whichever side. Hover cards never take focus.
- **Tray menu** (Settings… and Quit LinkPeek, as on macOS), approval confirmation and PAT removal use Fluent context menus and ContentDialog-style windows. Approve and Remove default to the safe **Cancel** button.
- Typography is Segoe UI Variable / Segoe UI through the WPF UI type ramp. Colors come from the Windows light, dark and high-contrast theme resources and the system accent color; open windows follow a Windows theme change.
- The process is per-monitor DPI aware (V2). WPF lays out in device-independent pixels and re-rasterizes on each display, so the same layout is used at 100%, 150% and 200%.

Windows 10 has no Mica; the same layout uses a solid theme background there.

### Saved PAT state

When a PAT exists in Windows Credential Manager for the organization being edited and the PAT input is empty, the field shows **eight dots in the placeholder color**, matching the macOS `PATSecureField`. LinkPeek only checks that the credential exists; it never reads the token to draw this, the dot count is always eight and does not reveal the token's length, and the dots are drawn over the field rather than placed in the input. The `PasswordBox` stays empty and the drawing has no UI Automation element. Typing a new PAT hides the dots immediately; **Save PAT** replaces the stored token. The credential card next to the field also says "PAT saved", and screen readers get the same information through the field's help text.

### UI checks

The **desktop-checks** CI job runs on a Windows runner and fails if any of these regress: a hyperlink exposed by a separate renderer process still resolves (while plain URL text and foreign windows are rejected); no text in Settings, the hover cards, dialogs or tray menu is clipped, squeezed or outside its window, in light and dark themes and at the minimum Settings size; the saved-PAT mask lines up with eight typed glyphs, uses a lighter placeholder tone, leaves the input empty, has no automation peer and disappears when typing starts; and the popover is placed against its link, on screen and on the correct side (below, above or beside, including near screen edges and on a short screen); closing Settings raises no unhandled exception; and opening and closing 200 hover cards leaks no windows, GDI/USER handles or theme reloads.

The job also uploads **LinkPeek-UI-previews** (kept 14 days): synthetic light/dark PNGs at 100% and 200% scale, for reviewing the interface without a Windows machine. They use a solid background instead of Mica and are not screenshots of a desktop; they never read saved credentials.

## Build or package

From this directory on Windows:

```powershell
dotnet test LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj
dotnet run --project LinkPeek.Windows/LinkPeek.Windows.csproj
./publish.ps1                          # x64 portable ZIP + SHA-256
./publish.ps1 -Runtime win-arm64       # ARM64 portable ZIP + SHA-256
```

The [Windows GitHub Actions workflow](https://github.com/nqnhut92/LinkPeek/actions/workflows/windows.yml) builds and tests both packages, runs the desktop checks above, and uploads workflow artifacts. Pull requests only produce artifacts. A push to `main` that passes both builds and the desktop checks also publishes the four Windows files to the **existing** GitHub release `v<version>` (for example `v0.1.1`), replacing earlier Windows files of the same name; it never creates a release or tag and never touches macOS files. If that release does not exist, nothing is published. A manual run can attach them to any other existing release; see [Releasing](../docs/RELEASING.md). The workflow does not sign the app. Packages are unsigned; company application-control policies or SmartScreen may require IT approval. Do not disable those controls. Keep the entire extracted folder, then run `LinkPeek.exe`; distributing the EXE alone omits its runtime dependencies.

Outputs are `artifacts/LinkPeek-Windows-<version>-win-x64.zip` (or `win-arm64`) and the matching `.zip.sha256`. The version comes from `LinkPeek.Windows.csproj`. Each ZIP includes `BUILD-INFO.txt` with the Windows version, architecture and exact source commit, plus usage and license files. Builds from a source archive without Git metadata report an unknown commit. Packaging uses a fresh staging directory to keep old dependencies out of a new archive.

## Download and verify

Prefer Windows assets in [Releases](https://github.com/nqnhut92/LinkPeek/releases). For an unpublished build, open **Actions → Windows → a successful run → Artifacts**, then choose `LinkPeek-Windows-win-x64` or `LinkPeek-Windows-win-arm64`. GitHub requires sign-in for Actions artifacts. The artifact download is an outer ZIP containing the application ZIP and checksum; extract both layers. A path such as `/workspace/...` in a cloud chat is internal and is not a public download link.

Download the matching `.zip.sha256` alongside a release ZIP. In PowerShell, from that folder:

```powershell
$zip = 'LinkPeek-Windows-0.2.0-win-x64.zip' # substitute the downloaded version/architecture
$expected = ((Get-Content "$zip.sha256" -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw 'SHA-256 mismatch; do not use this download.' }
'SHA-256 verified'
```

Matching checksums detect download corruption; they are not a code-signing identity.

Linux/macOS can run the core tests and cross-build the Windows app using the .NET SDK. They cannot launch the Windows desktop UI:

```sh
dotnet test LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj
dotnet build LinkPeek.Windows/LinkPeek.Windows.csproj
dotnet publish LinkPeek.Windows/LinkPeek.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

The cross-publish command creates an application folder. Use `publish.ps1` (PowerShell 7 is also available on Linux/macOS) to produce the versioned ZIP, checksum and build metadata.

## Use

1. Run the app. Right-click the LinkPeek tray icon (it may be under hidden icons).
2. Hover a link for 800 ms. Move onto the card to use it. It closes after leaving for 650 ms; Escape dismisses it. Copy Link and Copy QR close the card and suppress reopening until you leave the link. Win+Shift+S holds the card for up to 20 seconds; Escape ends the hold.
3. **Settings** controls the delay (500–1500 ms), optional Alt + hover, and launch at sign-in. **QR links** chooses which links open a QR card: Grab links (`app.grab.com/s/*`, on by default), your own `*` patterns, or any other link (off by default), plus a master switch. **Azure DevOps** has a switch for pull request cards. Sign-in registration uses only the current user's Run key, may be restricted by company policy, and refers to the current executable path. After moving the app, turn the option off and on again.

Generic web links need no account or network. QR generation preserves the full original URL including query and fragment. Links with embedded credentials and non-HTTP(S) schemes are rejected. Redirect URLs are encoded as exposed, not followed or unwrapped.

For Azure DevOps, enter an organization name and PAT in Settings; use **Save PAT**, then **Test connection**. PATs are stored per organization in Windows Credential Manager, never in `settings.json`, logs, or a saved setup script. PAT inputs are masked and saved tokens are never displayed; a saved PAT is indicated only by eight fixed placeholder dots (see [Saved PAT state](#saved-pat-state)). Test connection only verifies authenticated identity; it never votes. Use Code (Read & Write) scope for approval. **Remove** (after confirmation) deletes that organization's saved credential. Non-secret preferences are in `%LOCALAPPDATA%\LinkPeek\settings.json`.

PR previews show title, author, branches, lifecycle and your vote. Confirmation is on by default. Explicitly opting out changes the button to **Approve immediately** and shows a warning. Preparing and confirming approval revalidate the reviewer, eligible lifecycle and commit. Each confirmation is one-use and expires after two minutes. The app sends at most one vote request, then reads back the server's vote and commit before reporting success. Ambiguous outcomes require a refresh and are never automatically retried. Azure DevOps does not offer an atomic head-SHA precondition for votes, so a server-side commit race remains possible. Approved, completed, abandoned, draft and unknown-vote states are non-actionable. The app remembers verified approved heads only for the current session and displays a new-commits warning when they change. Each new hover fetches fresh state; continuous hover does not poll.

## Detection across applications

Detection uses Windows UI Automation and MSAA, with no application-name allowlist. It verifies that the pointer's native window belongs to the foreground top-level window, then examines a bounded neighborhood for hyperlink controls whose rectangles contain the pointer. A hosted WebView/Chromium renderer can have a different PID from the foreground app: this is allowed only within that same native root window. Inspection prioritizes up to eight ancestors of the hit text node; MSAA also checks hyperlink ancestors instead of only the exact text child. Full URL values or full URL hyperlink names are accepted; prose, friendly labels and image-source URLs are not scraped. The known scheme-less `dev.azure.com/` form is normalized only for accessibility hyperlinks. No links are invoked as part of inspection.

Teams, Outlook, browsers, PDF readers and other applications vary by version and accessibility provider. A displayed friendly name does not establish that its target is available to UI Automation. Test the particular apps used at your company; compatibility with every version of every app is not claimed. Elevated apps, protected surfaces and company policies may block access. LinkPeek does not request elevation or bypass those controls;

Inspection runs on one MTA worker away from the UI thread with a soft 350 ms UIA traversal budget, at most 24 visited UIA nodes, eight ancestor levels and three descendant levels. A misbehaving provider can still block a native call past that budget; no additional worker is spawned while it is pending, so the tray stays responsive but automatic hover can stall until the provider returns. Detection does not capture screen pixels or record application text. API requests use HTTPS to `dev.azure.com`, disable redirects/cookies and return sanitized errors.

### Running the desktop checks locally

From `Windows/` on a Windows development machine with a desktop session:

```powershell
dotnet run --project LinkPeek.DesktopChecks/LinkPeek.DesktopChecks.csproj -c Release -- artifacts/ui-previews
```

The harness starts a synthetic host window and a distinct child-renderer process. It is a regression test for the detection logic, not a statement about any particular app, and it cannot run on Linux.

## Windows acceptance checklist

Core tests, cross-compilation and the synthetic desktop checks do not validate real applications. Before a release, run through this list on a Windows machine:

- Launch a portable package without admin/.NET installed; verify one tray instance, Quit, and first-run Settings, and that hover still works while Settings is open.
- In each target app (including Teams, Edge/Chrome, Outlook and other daily tools), test raw URLs and friendly links. Record app/version and whether the target is exposed. Unsupported links must produce no guessed QR or PR.
- Test dwell, Alt mode, transfer from anchor to card, Escape, copy/reopen suppression, QR scanning including Unicode/query/fragment, clipboard contention, and Win+Shift+S hold.
- Test multiple displays, negative screen coordinates, 100%/150%/200% scaling and display changes. Ensure the card remains reachable and does not cover its anchor. Test light, dark and high-contrast themes (including switching while Settings is open), Mica on Windows 11, Windows 10 appearance, Narrator, and keyboard navigation through Settings, dialogs.
- Save/reopen/remove PAT; check Credential Manager and ensure settings JSON contains no token. With a saved PAT, the empty field shows eight placeholder-colored dots; typing hides them; another organization without a PAT shows an empty field. Test connection failure, wrong organization and offline behavior without submitting votes.
- On a disposable PR only, explicitly test confirmation/cancel, changed commit, inactive/draft/already-approved states, one vote per click, and server readback. Do not use a real work PR for smoke tests.
- Test launch at sign-in, move the folder and re-register, and confirm a policy-denied registration gives a visible error.

Accessibility-provider compatibility, native clipboard/credential integration, screenshot behavior and sign-in registration depend on the machine and apps, so they stay manual checks. This port does not migrate macOS Keychain contents or preferences, imitate macOS visual effects, or provide an installer/code-signing identity.

## Troubleshooting and removal

- **No window on launch:** the app normally starts in the tray, not the taskbar. Check the hidden-icon area; right-click the icon for Settings, or double-click it.
- **No card over a link:** try a full visible URL, check the Alt-hover setting and confirm the source app exposes a hyperlink target. A friendly label alone is insufficient. Do not change security settings or run elevated to work around it.
- **PR preview unavailable:** save a PAT for the exact organization in the URL and run Test connection. Check expiry, account permissions and network access to `https://dev.azure.com`. QR features continue working offline.
- **Blocked launch or sign-in option:** your company's application-control/registry policy may require IT assistance. Portable packaging does not override company policy.
- **LinkPeek stopped or showed "recovered from an unexpected error":** open `%LOCALAPPDATA%\LinkPeek\crash.log`. It holds exception types, messages and stack traces with a timestamp and version, never PATs, URLs or chat text, and at most 20 entries per session. Include it when reporting a problem. If LinkPeek ended without a log entry, check **Event Viewer → Windows Logs → Application** for a `.NET Runtime` or `Application Error` event from `LinkPeek.exe`.
- **Remove the app:** disable launch at sign-in, remove saved PATs in Settings (or remove `LinkPeek:AzureDevOps:<organization>` entries in Windows Credential Manager), quit, then delete the extracted app folder. Delete `%LOCALAPPDATA%\LinkPeek` only if you also want to remove preferences and the crash log. Deleting the app folder alone does not remove credentials.
