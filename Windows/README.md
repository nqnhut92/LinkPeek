# LinkPeek for Windows

Native Windows port of LinkPeek: a tray utility that previews web links as offline QR codes or Azure DevOps pull requests. The existing Swift/macOS application remains in the repository root; Windows code lives here.

Targets Windows 10 22H2 / Windows 11, x64 or ARM64. Development requires the .NET 10 SDK. The published portable app includes its runtime and runs as the current user, without administrator privileges or a separate .NET installation. The initial build was reported to work in other apps and Teams web, but not Teams desktop. Windows 0.1.1 is still being finalized: the current build updates hosted-renderer detection and the interface; **actual desktop Teams compatibility and the refreshed UI still need user validation**. See the acceptance checklist below and [Windows changes](CHANGELOG.md).

## Interface

The Windows interface uses a compact borderless hover card, Segoe UI typography, accent actions, rounded controls, and a sidebar with General, Azure DevOps and About pages. Windows 11 supplies rounded outer-window corners and native dark title bars where supported. Windows 10 keeps the same control layout with the older system window frame. High contrast uses Windows system colors. The app's light/dark preference is read when a window opens; close and reopen Settings or the card after changing the Windows app theme.

Windows CI renders ten PNG previews of the three Settings pages, QR card and PR card in light/dark themes. Download **LinkPeek-UI-previews** from a successful Actions run to review them. These are drawn from synthetic controls and data; the preview renderer does not take a screenshot of the user's desktop or read saved credentials.

## Build or package

From this directory on Windows:

```powershell
dotnet test LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj
dotnet run --project LinkPeek.Windows/LinkPeek.Windows.csproj
./publish.ps1                          # x64 portable ZIP + SHA-256
./publish.ps1 -Runtime win-arm64       # ARM64 portable ZIP + SHA-256
```

The [Windows GitHub Actions workflow](https://github.com/nqnhut92/LinkPeek/actions/workflows/windows.yml) builds and tests both packages, checks a synthetic cross-process hosted hyperlink, renders UI previews, and uploads workflow artifacts. Pushes and pull requests only produce artifacts. A manual run can also attach the Windows assets to a specified **existing** GitHub release, after both builds and desktop checks pass; see [Releasing](../docs/RELEASING.md). Nothing is released automatically on push, and the workflow does not sign the app. Packages are unsigned; company application-control policies or SmartScreen may require IT approval. Do not disable those controls. Keep the entire extracted folder, then run `LinkPeek.exe`; distributing the EXE alone omits its runtime dependencies.

Outputs are `artifacts/LinkPeek-Windows-<version>-win-x64.zip` (or `win-arm64`) and the matching `.zip.sha256`. The version comes from `LinkPeek.Windows.csproj`. Each ZIP includes `BUILD-INFO.txt` with the Windows version, architecture and exact source commit, plus usage and license files. Builds from a source archive without Git metadata report an unknown commit. Packaging uses a fresh staging directory to keep old dependencies out of a new archive.

## Download and verify

Prefer Windows assets in [Releases](https://github.com/nqnhut92/LinkPeek/releases). For an unpublished build, open **Actions → Windows → a successful run → Artifacts**, then choose `LinkPeek-Windows-win-x64` or `LinkPeek-Windows-win-arm64`. GitHub requires sign-in for Actions artifacts. The artifact download is an outer ZIP containing the application ZIP and checksum; extract both layers. A path such as `/workspace/...` in a cloud chat is internal and is not a public download link.

Download the matching `.zip.sha256` alongside a release ZIP. In PowerShell, from that folder:

```powershell
$zip = 'LinkPeek-Windows-0.1.1-win-x64.zip' # substitute the downloaded version/architecture
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
2. Hover a link for 500 ms. Move onto the card to use it. It closes after leaving for 650 ms; Escape dismisses it. Copy Link and Copy QR close the card and suppress reopening until you leave the link. Win+Shift+S holds the card for up to 20 seconds; Escape ends the hold.
3. **Settings** controls the delay (300–1000 ms), optional Alt + hover, and launch at sign-in. Sign-in registration uses only the current user's Run key, may be restricted by company policy, and refers to the current executable path. Re-save after moving the app. **Pause hover** temporarily stops detection.
4. **Open link…** accepts an explicitly pasted URL when an app does not expose its target. It does not read the clipboard automatically. That card stays open until closed or copied.

Generic web links need no account or network. QR generation preserves the full original URL including query and fragment. Links with embedded credentials and non-HTTP(S) schemes are rejected. Redirect URLs are encoded as exposed, not followed or unwrapped.

For Azure DevOps, enter an organization name and PAT in Settings; use **Save PAT**, then **Test connection**. PATs are stored per organization in Windows Credential Manager, never in `settings.json`, logs, or a saved setup script. PAT inputs are masked and saved tokens are never displayed. Test connection only verifies authenticated identity; it never votes. Use Code (Read & Write) scope for approval. **Remove PAT** deletes that organization's saved credential. Non-secret preferences are in `%LOCALAPPDATA%\LinkPeek\settings.json`.

PR previews show title, author, branches, lifecycle and your vote. Confirmation is on by default. Explicitly opting out changes the button to **Approve immediately** and shows a warning. Preparing and confirming approval revalidate the reviewer, eligible lifecycle and commit. Each confirmation is one-use and expires after two minutes. The app sends at most one vote request, then reads back the server's vote and commit before reporting success. Ambiguous outcomes require a refresh and are never automatically retried. Azure DevOps does not offer an atomic head-SHA precondition for votes, so a server-side commit race remains possible. Approved, completed, abandoned, draft and unknown-vote states are non-actionable. The app remembers verified approved heads only for the current session and displays a new-commits warning when they change. Each new hover fetches fresh state; continuous hover does not poll.

## Detection across applications

Detection uses Windows UI Automation and MSAA, with no application-name allowlist. It verifies that the pointer's native window belongs to the foreground top-level window, then examines a bounded neighborhood for hyperlink controls whose rectangles contain the pointer. A hosted WebView/Chromium renderer can have a different PID from the foreground app: this is allowed only within that same native root window. Inspection prioritizes up to eight ancestors of the hit text node; MSAA also checks hyperlink ancestors instead of only the exact text child. Full URL values or full URL hyperlink names are accepted; prose, friendly labels and image-source URLs are not scraped. The known scheme-less `dev.azure.com/` form is normalized only for accessibility hyperlinks. No links are invoked as part of inspection.

Teams, Outlook, browsers, PDF readers and other applications vary by version and accessibility provider. A displayed friendly name does not establish that its target is available to UI Automation. Test the particular apps used at your company; no cross-app compatibility is claimed yet. Elevated apps, protected surfaces and company policies may block access. LinkPeek does not request elevation or bypass those controls; use **Open link…** if needed.

Inspection runs on one MTA worker away from the UI thread with a soft 350 ms UIA traversal budget, at most 24 visited UIA nodes, eight ancestor levels and three descendant levels. A misbehaving provider can still block a native call past that budget; no additional worker is spawned while it is pending, so the tray and manual entry stay responsive but automatic hover can stall until the provider returns. Detection does not capture screen pixels or record application text. API requests use HTTPS to `dev.azure.com`, disable redirects/cookies and return sanitized errors.

### Diagnose desktop Teams or another unsupported app

1. Right-click the tray icon and choose **Diagnose hovered link…**.
2. Read the notice, click OK, then move onto the problem link within three seconds. Keep the source app foreground and the pointer still.
3. A report opens with roles, PID relationships, node sizes, whether candidate fields contain complete URLs, and the resolution path or timeout. It does **not** contain URL values, friendly link labels, chat content, PATs or account details. Nothing is uploaded or saved automatically.
4. Use **Copy report** if you choose to share that diagnostic text. If a provider is stalled, restart LinkPeek before another attempt. The diagnostic path shares the single-worker limit.

New Teams desktop uses a hosted renderer and can expose a different accessibility tree from Teams web. The current 0.1.1 changes address a confirmed limitation in LinkPeek's old same-PID filter and expand hyperlink ancestry inspection; they are not proof of compatibility with every Teams build. A complete URL visibly shown in a chat still has to be exposed as a hyperlink target by that build.

### Native regression and preview checks

From `Windows/` on a Windows development machine:

```powershell
dotnet run --project LinkPeek.DesktopChecks/LinkPeek.DesktopChecks.csproj -c Release -- artifacts/ui-previews
```

The harness starts a synthetic host window and a distinct child-renderer process, then checks that a hyperlink resolves through the host while plain URL text and a foreign root window are rejected. It also renders the ten light/dark UI previews. The synthetic fixture is not Teams and does not establish real Teams behavior. The harness requires a Windows desktop session, and its checks cannot run in Linux.

For only the synthetic appearance previews, the Windows app also accepts `LinkPeek.exe --render-ui <output-folder>`; this mode does not start the tray workflow, mutate preferences/startup settings, access PATs or call Azure DevOps.

## Windows acceptance checklist

Core tests and cross-compilation do not validate actual desktop behavior. Before distributing, run on a Windows machine:

- Launch a portable package without admin/.NET installed; verify one tray instance, Pause, Quit, and first-run Settings.
- In each target app (including Teams, Edge/Chrome, Outlook and other daily tools), test raw URLs and friendly links. Record app/version and whether the target is exposed. Unsupported links must produce no guessed QR or PR. Confirm manual entry works.
- Test dwell, Alt mode, transfer from anchor to card, Escape, copy/reopen suppression, QR scanning including Unicode/query/fragment, clipboard contention, and Win+Shift+S hold.
- Test multiple displays, negative screen coordinates, 100%/150%/200% scaling and display changes. Ensure the card remains reachable and does not cover its anchor. Test system/high-contrast appearance and keyboard access through manual entry.
- Save/reopen/remove PAT; check Credential Manager and ensure settings JSON contains no token. Test connection failure, wrong organization and offline behavior without submitting votes.
- On a disposable PR only, explicitly test confirmation/cancel, changed commit, inactive/draft/already-approved states, one vote per click, and server readback. Do not use a real work PR for smoke tests.
- Test launch at sign-in, move the folder and re-register, and confirm a policy-denied registration gives a visible error.

Windows 11 appearance, accessibility-provider compatibility, native clipboard/credential integration, screenshot behavior and sign-in registration remain manual validation requirements. This port does not migrate macOS Keychain contents or preferences, implement macOS visual effects, or provide an installer/code-signing identity.

## Troubleshooting and removal

- **No window on launch:** the app normally starts in the tray, not the taskbar. Check the hidden-icon area; right-click the icon for Settings, or double-click it.
- **No card over a link:** try a full visible URL, check Pause/Alt settings and confirm the source app exposes a hyperlink target. A friendly label alone is insufficient. Use Open link… rather than changing security settings or running elevated.
- **PR preview unavailable:** save a PAT for the exact organization in the URL and run Test connection. Check expiry, account permissions and network access to `https://dev.azure.com`. QR features continue working offline.
- **Blocked launch or sign-in option:** your company's application-control/registry policy may require IT assistance. Portable packaging does not override company policy.
- **Remove the app:** disable launch at sign-in, remove saved PATs in Settings (or remove `LinkPeek:AzureDevOps:<organization>` entries in Windows Credential Manager), quit, then delete the extracted app folder. Delete `%LOCALAPPDATA%\LinkPeek` only if you also want to remove preferences. Deleting the app folder alone does not remove credentials.
