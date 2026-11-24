# AGENTS.md

## 1. Project Overview & Stack

- **Purpose**: Cross-platform native utility that previews hovered web links as offline QR codes and Azure DevOps pull request status/approval cards from the macOS menu bar or Windows system tray.
- **Stack**:
  - **macOS**: Swift 6, SwiftUI, AppKit, SwiftPM (`Package.swift`), Xcode (`LinkPeek.xcodeproj`), macOS 13+. Zero third-party Swift dependencies.
  - **Windows**: C#, .NET 10 (`net10.0` / `net10.0-windows`), WPF, `WPF-UI` 4.3.0, `QRCoder` 1.6.0, xUnit (`LinkPeek.Core.Tests`), Windows 10 22H2 / Windows 11 (x64 & ARM64).
  - **Storage**: Local OS credential stores (macOS Keychain / Windows Credential Manager) and local preferences (`UserDefaults` / `%LOCALAPPDATA%\LinkPeek\settings.json`). No database.
  - **Release / CI**: Shell + Python (`scripts/release.sh`, `scripts/tests/test_release.py`) for macOS; PowerShell + GitHub Actions (`Windows/publish.ps1`, `.github/workflows/windows.yml`) for Windows.
- **Docs Map**:
  - [docs/TECHNICAL_DESIGN.md](docs/TECHNICAL_DESIGN.md) — macOS architecture, settings, AX/dwell rules, QR generation, Azure DevOps API & approval safeguards, verification, and release packaging.
  - [docs/RELEASING.md](docs/RELEASING.md) — Independent macOS/Windows release packaging, checksums, and GitHub Release asset rules.
  - [Windows/README.md](Windows/README.md) — Windows UI, build/publish, desktop checks, UIA detection, acceptance checklist, and troubleshooting.
  - [Windows/CHANGELOG.md](Windows/CHANGELOG.md) — Windows port release history.

## 2. Essential Commands

- **Build**:
  - macOS app (unsigned Debug): `xcodebuild -project LinkPeek.xcodeproj -scheme LinkPeek -configuration Debug CODE_SIGNING_ALLOWED=NO build`
  - macOS app (unsigned Release): `xcodebuild -project LinkPeek.xcodeproj -scheme LinkPeek -configuration Release CODE_SIGNING_ALLOWED=NO build`
  - macOS package core: `swift build`
  - Windows app (cross-build or native): `dotnet build Windows/LinkPeek.Windows/LinkPeek.Windows.csproj`
  - Windows desktop UI/UIA checks (Windows desktop session only): `dotnet run --project Windows/LinkPeek.DesktopChecks/LinkPeek.DesktopChecks.csproj -c Release -- Windows/artifacts/ui-previews`
- **Test (Single)**:
  - macOS Swift Testing filter: `swift test --filter <SuiteOrTestName>` (e.g. `swift test --filter LinkFilterTests`)
  - Windows xUnit filter: `dotnet test Windows/LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj --filter <FullyQualifiedNameOrClass>`
  - Release script single test: `python3 -m unittest scripts.tests.test_release.<TestClass>.<test_method>`
- **Test (Full)**:
  - macOS core tests: `swift test`
  - Release script tests: `python3 -m unittest discover -s scripts/tests`
  - Windows core tests: `dotnet test Windows/LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj`
- **Lint / Format / Typecheck**:
  - Swift strict format check: `swift format lint --strict --recursive Sources Tests Package.swift`
  - Windows warnings-as-errors & nullable check: `dotnet build Windows/LinkPeek.Core/LinkPeek.Core.csproj && dotnet build Windows/LinkPeek.Windows/LinkPeek.Windows.csproj`

## 3. Hard Constraints & Guardrails

- **Never**:
  - Never send hovered URLs to external servers during QR generation; QR previews must remain 100% offline.
  - Never submit live Azure DevOps PR votes in CLI tests or validation, and never auto-retry failed or ambiguous PR approval mutations.
  - Never log, store in `UserDefaults`/`settings.json`, or prefill/display PATs; saved-PAT indicators must stay synthetic 8-character placeholders without reading the secret.
  - Never commit signing material (`*.p12`, `*.pfx`, `*.key`, `*.cer`, `*.crt`, `*.pem`), `$HOME` paths, or email addresses in signing metadata (`scripts/release.sh` enforces this).
  - Never alter macOS release assets or move existing release tags from the Windows CI workflow.
- **Always**:
  - Always run macOS AX inspection on the main queue (`Sources/Inspector.swift`) and Windows UIA inspection on the dedicated MTA worker (`Windows/LinkPeek.Windows/LinkInspector.cs`), keeping traversal bounded (24 nodes, 350 ms soft deadline).
  - Always require explicit confirmation by default before Azure DevOps PR approval, revalidating lifecycle, reviewer identity, existing vote, and head SHA immediately before the single `PUT`, then reading back server state.
  - Always keep `Package.swift` `exclude` (`App.swift`, `Settings.swift`, `HoverCardView.swift`) and `sources` lists synchronized when adding or removing `Sources/` files.
  - Always preserve `com.prhover.app` preference and Keychain migration compatibility on macOS.
- **Gotchas**:
  - `LinkPeek.xcodeproj` has no test target; `swift test` only compiles the `Package.swift` library target and excludes AppKit/SwiftUI app entrypoints.
  - `swift test` skips `realKeychainSyntheticRoundTrip()` in sandboxed/non-interactive environments.
  - `Windows/` targets `.NET 10` (`Windows/global.json`); `dotnet` may not be installed on macOS hosts, and `LinkPeek.DesktopChecks` cannot run on Linux/macOS.
  - `.github/workflows/windows.yml` only triggers on pushes to `main` touching `Windows/**`, `Resources/AppIcon.png`, `LICENSE`, or the workflow file itself (tag pushes do not trigger it).

## 4. Code Style & Patterns

- Keep zero third-party Swift package dependencies in the macOS app; use Apple system frameworks and SF Symbols.
- Keep Windows UI aligned with Fluent Design (`WPF-UI`) and per-monitor DPI awareness (V2); apply settings immediately without a Save button.
- Sanitize all network, Keychain/Credential Manager, and system errors before displaying or logging; never include tokens, response bodies, or hovered URLs in crash/debug logs.
- Keep `LinkFilter` case-insensitive `host/path` glob matching independent of scheme, query, and port; reject non-HTTP(S) schemes and URLs with embedded credentials.

## 5. Definition of Done

- **Verification**:
  - macOS changes: run `swift test`, `swift format lint --strict --recursive Sources Tests Package.swift`, and `xcodebuild -project LinkPeek.xcodeproj -scheme LinkPeek -configuration Debug CODE_SIGNING_ALLOWED=NO build` (plus `python3 -m unittest discover -s scripts/tests` if `scripts/` changed).
  - Windows changes: run `dotnet test Windows/LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj` and `dotnet build Windows/LinkPeek.Windows/LinkPeek.Windows.csproj` (and `LinkPeek.DesktopChecks` + [Windows acceptance checklist](Windows/README.md#windows-acceptance-checklist) on a Windows machine for UI/UIA changes).
  - Docs-only changes: verify all referenced file paths, commands, and links exist and match the codebase.
- **Documentation**: Update [docs/TECHNICAL_DESIGN.md](docs/TECHNICAL_DESIGN.md), [docs/RELEASING.md](docs/RELEASING.md), or [Windows/README.md](Windows/README.md) whenever architecture, build/release steps, or platform behavior changes.
- If the Nicholas Hub MCP is available, sync the repository architecture via `arch_document` after new commits (skip if unavailable).
