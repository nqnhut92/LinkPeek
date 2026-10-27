# LinkPeek

LinkPeek previews web links as offline QR codes and Azure DevOps pull requests. It runs in the macOS menu bar or the Windows system tray. Approval requires explicit confirmation by default and always requires server verification. Hover detection depends on the foreground application exposing the actual link target through the platform's accessibility APIs.

Both native implementations live in this repository and have separate build pipelines:

| Platform | Requirements | Source and development | Download asset |
| --- | --- | --- | --- |
| macOS | macOS 13+; Xcode with Swift 6 to build | `Sources/`, `Tests/`, `LinkPeek.xcodeproj`; instructions below | `LinkPeek-<version>.zip` |
| Windows | Windows 10 22H2 / Windows 11, x64 or ARM64; .NET 10 SDK to build | [`Windows/`](Windows/README.md) | `LinkPeek-Windows-<version>-win-x64.zip` or `-win-arm64.zip` |

The Windows portable package includes its .NET runtime and needs no installer or administrator privileges. Windows 0.1.1 is still being finalized. Hover detection works across apps, including Teams desktop; the interface now uses Fluent Design controls (WPF UI) and awaits user validation. See the [Windows changelog](Windows/CHANGELOG.md). Windows uses QRCoder and WPF UI; the macOS app has no third-party dependencies. Native Liquid Glass is available on macOS 26+.

## Install on Windows

1. Open [Releases](https://github.com/nqnhut92/LinkPeek/releases) and expand **Assets**. Choose the Windows **x64** ZIP for Intel/AMD computers or **ARM64** for Windows on ARM. Do not download GitHub's automatic **Source code** ZIP to run the app.
2. Extract the **entire** ZIP to a stable folder and run `LinkPeek.exe`. Keep the DLLs and runtime files beside it. The unsigned app may require company IT approval under application-control policy.
3. Right-click its tray icon (possibly under hidden icons) for **Settings**, **Open link…**, **Pause hover** and **Quit**. QR generation works offline without a PAT. Optional Azure DevOps setup uses Windows Credential Manager.
4. To update, quit the app, extract the new package into a fresh folder and launch it. Re-enable launch at sign-in in Settings if you moved the executable. Settings and credentials remain under your Windows user account.

If Windows assets are not published yet, download a completed build from [Actions → Windows](https://github.com/nqnhut92/LinkPeek/actions/workflows/windows.yml). GitHub requires sign-in to download Actions artifacts. Extract the downloaded artifact wrapper, then the application ZIP inside it. See [Windows instructions](Windows/README.md) for usage, checksums, limitations and troubleshooting, and the [release guide](docs/RELEASING.md) for adding Windows assets to an existing macOS release.

## Install on macOS

1. Download `LinkPeek-<version>.zip` from [GitHub Releases](https://github.com/nqnhut92/LinkPeek/releases), unzip it, and move `LinkPeek.app` to **Applications**. The source-code ZIP is for developers, not the runnable app.
2. Open the app. Releases are locally self-signed, not notarized by Apple. If macOS blocks the first launch, dismiss the alert, then use **System Settings → Privacy & Security → Open Anyway** and confirm, after checking that you downloaded the app from this repository.
3. In **Link menu → Settings… → General → Grant access…**, enable Accessibility for LinkPeek. QR features need no PAT. Azure DevOps features require the organization and PAT described below.
4. Optionally enable **Settings → General → Launch at login**.

To update, quit LinkPeek and replace the app in Applications. Release builds use a stable signing identity to help preserve granted permissions across updates. A development or ad-hoc build has a different signing identity and may require granting permissions again. About displays the running version and build.

Each release ZIP has a matching `.sha256` file. To check a download, put both files in the same folder and run `shasum -a 256 -c LinkPeek-0.1.1.zip.sha256` there (substitute the release version).

## Run on macOS

Open **`LinkPeek.xcodeproj`** directly (rather than the folder or `Package.swift`), select **LinkPeek → My Mac**, and Run. Quit other copies first. Configure a stable signing identity for ongoing use.

Use **Link menu → Settings… → General → Grant access…** and enable the running app. Open **Link menu → Settings… → Azure DevOps**, enter the Azure DevOps organization name and save a PAT with **Code (Read & Write)** scope. PATs remain in this Mac's Keychain. **Test Connection** verifies identity without submitting a vote.

Hover an HTTP or HTTPS link in any app that exposes its target URL through Accessibility. QR generation requires no PAT or network request. Use **Copy Link** to copy the full URL or **Copy QR** to copy the PNG image. Either successful copy closes the popover and prevents reopening until the pointer leaves the link. For Azure DevOps PR features, hover a PR link. The app must expose the actual link URL through Accessibility; friendly labels without exposed targets are not supported. Redirect links produce a QR of the exposed URL and are not unwrapped for PR previews. The default dwell is 500 ms; Option + Hover is optional. Approved, completed and draft states are non-actionable. **Approve → Confirm Approve** (default) revalidates the PR and commit before submitting one vote. The Azure DevOps API cannot guarantee atomic approval against a head SHA; see the design document for constraints.

## macOS development

`Sources/` contains app code; `Assets.xcassets/`, `Resources/` and `Info.plist` sit at the root. `Tests/` contains regression coverage. Xcode builds the application; `Package.swift` exposes the testable core.

```sh
swift test
swift format lint --strict Sources/*.swift Tests/*.swift Package.swift
python3 -m unittest discover -s scripts/tests -p 'test_*.py'
```

Build outputs, local caches and signing-certificate exports are ignored and should not be committed. CLI checks do not replace runtime checks for hover, focus, clipboard and permissions.

See [technical design](docs/TECHNICAL_DESIGN.md) for architecture, UI behavior, credential handling, approval safeguards, asset provenance and verification status.

Windows architecture and validation are documented in [Windows/README.md](Windows/README.md). Maintainers can build both platform packages independently and follow [the release guide](docs/RELEASING.md) to publish them together or separately.

To capture a region, keep the popup open and press your macOS region-capture or Screenshot-options shortcut (defaults: **⌘⇧4** / **⌘⇧5**). LinkPeek holds the card while the system screenshot overlay is open, then waits one second after that overlay closes before dismissing it. Escape dismisses immediately; 20 seconds is a fallback limit. The screenshot is taken by macOS. Debug diagnostics remain available through the `--debug` launch argument.

Each new hover dwell fetches current PR state from Azure DevOps, including external vote resets. Move off the link and hover again to refresh; continuous hover does not poll.

**Skip approval confirmation** is OFF by default. When enabled, the popup warns that clicking Approve submits immediately. A deliberate click still revalidates PR state, identity and commit, sends one vote, and verifies the response.

Enable **Settings → General → Launch at login** to start LinkPeek when signing in. This is off until enabled, uses the native macOS login-item service, and reflects changes made in System Settings. If approval is required, Settings provides an Open Login Items button. Use a signed app at a stable installed location for ongoing use.

## macOS app identity and version

The project, scheme, executable, Swift module and application are named **LinkPeek** (`LinkPeek.xcodeproj`, `LinkPeek.app`). The bundle identifier is `com.linkpeek.app`. The marketing version is separate from the timestamp build number; About shows `Version 0.1.1 (build-YYYYMMDD.HHMMSS)`, read from the built bundle.

When upgrading from PR Hover, known preferences are copied once where LinkPeek has no saved value. The original Keychain service is retained to preserve PATs. Grant Accessibility to the renamed app and enable Launch at login for LinkPeek if needed. Quit old copies before running the renamed app.

## License

LinkPeek is licensed under the [MIT License](LICENSE). The macOS app also shows it in Settings → About → MIT License. Windows packages include the project license, QRCoder and WPF UI notices and bundled .NET runtime licenses.
