# LinkPeek technical design

## Application and build

LinkPeek is an accessory macOS app with a link symbol in the menu bar. Opening Settings waits for the status menu's close callback to finish before requesting presentation. It temporarily uses regular activation policy and orders the window front before requesting activation. If already active, it makes Settings key immediately; otherwise the activation notification restores key/main status. An attached sheet retains key focus. Dock reopen requests show Settings again. Closing Settings returns to accessory mode.

`LinkPeek.xcodeproj` builds the `LinkPeek` scheme, target, executable and `LinkPeek.app`. The Swift package and testable library module also use `LinkPeek`. The bundle identifier is `com.linkpeek.app`. The Xcode scheme has no test target; `swift test` exercises the package core and excludes the app/UI source.

The project uses `PRODUCT_NAME = $(TARGET_NAME)`, marketing version `0.1.1`, and a build phase stamps the built bundle's `CFBundleVersion` with the local build time (`YYYYMMDD.HHMMSS`). About reads that bundle metadata and displays `Version 0.1.1 (build-YYYYMMDD.HHMMSS)`. The source build fallback remains `1`.

The approved icon is a petrol blue rounded square with two white chain links and a small preview card. It was generated and prepared with ImageGen. `Resources/AppIcon.png` is the 1024px master; `Assets.xcassets/AppIcon.appiconset` contains all ten macOS size/scale variants. No third-party brand logo is embedded.

## Settings

Settings uses a sidebar and grouped cards with adaptive primary text, system blue actions/selection and system green switches. General contains launch at login, Accessibility, hover mode and dwell delay. Azure DevOps contains organization/PAT management, connection testing and approval options. About contains the app icon, bundle-backed version, feature summary and Quit. About links to `https://github.com/nqnhut92/LinkPeek` with GitHub and Quit capsule buttons.

Launch at login uses `SMAppService.mainApp` only when explicitly enabled. Settings reflects its status, offers Login Items approval when required, and reports sanitized errors. It does not register automatically.

## Rename compatibility

Known preferences (`organization`, `optionHover`, `delayMS`, `skipConfirmation`, `successNotification`) migrate from `com.prhover.app` only when the new domain lacks that key. New values are never overwritten. The old Keychain service `com.prhover.app.azure-pat` is intentionally retained so PATs remain discoverable; secrets are not copied into preferences. Tests verify the bounded migration and precedence.

The changed bundle identity may require granting Accessibility again and enabling LinkPeek's login item. The old app must be quit before launching the renamed app. Neither permissions nor login items are changed by build validation.

## Accessibility and dwell

Any foreground app is eligible, except LinkPeek itself. App name or bundle identifier is not an allowlist. AX inspection runs on the main queue because system-wide hit testing can enter AppKit in-process accessibility code that requires it. There is one request in flight, a 120ms timer and stationary-pointer refresh no more often than every 800ms. Traversal is bounded to 24 nodes, depth 3, eight children per node, a 150ms messaging timeout and a 350ms soft deadline.

Candidates must expose complete HTTP/HTTPS URLs with no embedded credentials, contain the cursor and have bounded geometry. AXValue fallback requires AXLink. Existing scheme-less Teams normalization is limited to `dev.azure.com`. Message bodies are not scraped. Missing targets produce no card; actual URL exposure depends on the app. Debug diagnostics redact URL and message content.

Pointer/PID checks discard stale results. Dwell identity includes the exact exposed URL, query and fragment. Default delay is 500ms, configurable from 300–1000ms. Option + Hover also requires holding Option. The existing 650ms exit grace allows entering the card. Generation tickets reject stale asynchronous PR responses. Each new PR dwell fetches fresh server state; continuous hover does not poll.

## Native cards and QR

`NSPopover` owns the continuous system surface, pointer and shadow. A transparent, nonactivating, mouse-ignoring anchor panel maps the external AX rectangle to the NSView required by AppKit. macOS 26 uses full-size content. Animation and detachment are disabled; application-defined behavior retains explicit hover/capture dismissal. Content and chrome inherit system appearance, and text/icons use semantic colors. Pointer retention uses the actual popover window frame.

Strictly recognized Azure DevOps PR links display only PR preview. Other web links display only QR. There is no QR/PR switch. QR generation is offline using Core Image with integer module scaling and a four-module white quiet zone. PNG round-trip coverage verifies the exact full URL. Copy Link appears left of Copy QR and writes the full exposed URL as a pasteboard string; Copy QR writes PNG. Writes occur only on a click. Either successful copy closes the card and suppresses reopening until the pointer leaves the source link. Copy Link remains available when QR generation fails. Failure leaves a safe error. Redirect URLs are encoded as exposed and are not unwrapped for PR preview.

System region-capture and Screenshot-options shortcuts hold an already-visible card during the screenshot overlay, with a 20-second fallback. Escape dismisses immediately. Observed overlay exit waits one second before closing, then suppresses same-anchor reopening. Shortcut preferences are read-only and respect remaps/disabled entries. Window-owner metadata is inspected without capturing screen pixels. The system performs the screenshot.

## Credentials and Azure DevOps

PATs are stored per organization in this Mac's Keychain and are never prefilled or displayed. Both newly entered tokens and the saved-state indicator use native `NSSecureTextFieldCell` masking with matching geometry. The saved-state indicator uses adaptive secondary gray; entered tokens use the normal text color. An eight-character synthetic sample is drawn only as the saved-state indicator; it never enters the token binding, reads the stored credential, or exposes its length. Credential indicators request metadata only. Organization and UX preferences are stored in UserDefaults. Test Connection verifies authenticated identity without voting. Code (Read & Write) scope is required for approval.

PR requests use HTTPS at the exact `dev.azure.com` host, encoded path segments, ephemeral URLSession configuration, no cookies/HTTP cache and no redirects. Requests have bounded timeouts. REST details/reviewers/votes use API 7.1; connectionData is unversioned. Errors are sanitized; tokens, response bodies and raw network errors are not logged. Only the configured organization can receive authenticated PR requests.

Cards distinguish lifecycle and the authenticated reviewer's exact vote. Eligible active, non-draft PRs with votes 0/-5/-10 permit approval; votes 5/10 and completed, abandoned, draft or unknown states are non-actionable. New Commits uses only this app's verified reviewed-head record, scoped to reviewer and cleared on invalidation/restart.

## Approval safeguards

Hovering never votes. By default, Approve prepares a one-use confirmation after a fresh PR/head/identity check; Confirm Approve authorizes mutation. Explicit Skip confirmation opt-in permits the initial Approve click to use the same verified flow directly. Confirmations expire after two minutes and cannot be replayed or redirected.

Immediately before the single PUT, the service revalidates lifecycle, reviewer, existing vote and commit using a captured credential context. A fresh read must confirm vote 10 and the same head before reporting success. Failed/ambiguous mutations are never automatically retried. Azure DevOps has no atomic head-SHA precondition, so revalidation narrows but cannot eliminate a server-side commit race. No live vote is submitted during CLI validation.

## Verification

CLI checks cover Swift package regression tests, unsigned Debug/Release app builds, strict Swift formatting and project/plist lint. Core tests do not validate AppKit focus, native appearance, physical display positioning, clipboard paste behavior, Accessibility URL exposure or login launch. Those require signed runtime checks. The user owns visual confirmation; CLI builds do not launch Xcode or the app.

The latest code validation passed 43 Swift regression tests and unsigned Debug/Release builds. The user reported the Settings focus change provisionally acceptable. Native hover, focus, clipboard and permissions still require testing the actual packaged app; neither core tests nor signature verification establish runtime behavior. Release-script tests use temporary fake tools and never access real signing keys.

## Release packaging

`scripts/release.sh` creates an unsigned archive, then signs it locally with a self-signed identity. The default identity is `LinkPeek Release`, selected from Keychain; `LINKPEEK_SIGNING_IDENTITY` overrides it. No paid Developer ID or notarization is used. Certificate exports are ignored by Git and are never imported or bundled by the script. The signing certificate and private key must already be in Keychain. Keeping the same identity and bundle identifier helps maintain permission continuity; switching from development/ad-hoc builds can require granting permissions again.

Each run retains a separate archive under `build/release/archive.*`. Embedded dylibs are signed before the app; the app uses hardened runtime with no custom entitlements. Signature verification and checks for home-directory paths and email addresses in signing-authority metadata must pass before producing the versioned ZIP and SHA-256 file. Local ad-hoc mode (`LINKPEEK_SIGNING_IDENTITY=-`) uses an `-adhoc` filename suffix. It is a packaging test, not the public release identity.

Release outputs and archives are ignored. The script neither commits source nor creates tags nor publishes to GitHub. The maintainer tests the packaged app, commits and pushes matching source, and attaches the ZIP/checksum to a version-tagged GitHub Release. Installation requires the first-launch Open Anyway flow for this unnotarized app; the README documents it.

## Publication and licensing

LinkPeek is licensed under MIT, copyright © 2026 Nicholas Nguyen. The root `LICENSE` is copied into the application bundle, and Settings → About → MIT License displays that exact bundled text. README links to the same license.

There are no third-party Swift package dependencies or bundled third-party fonts. System frameworks and SF Symbols are used through Apple APIs; their rights are not transferred by the app's source license. The generated app icon's provenance is recorded above.

The MIT permission and warranty notices are retained in full in the repository and distributed app. No third-party QR attribution was identified by the review. Test URLs use synthetic organization, project and repository names.
