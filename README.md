# LinkPeek

LinkPeek previews hovered web links as offline QR codes and Azure DevOps pull request status cards from the macOS menu bar or Windows system tray.

## Tech Stack

- **macOS**: Swift 6, SwiftUI & AppKit (`LinkPeek.xcodeproj`, `Package.swift`), macOS 13+
- **Windows**: C#, .NET 10, WPF & WPF UI 4.3.0 (`Windows/`), Windows 10 22H2 / Windows 11 (x64 & ARM64)
- **Storage**: macOS Keychain / Windows Credential Manager for PATs; local preferences (`UserDefaults` / `settings.json`), no database

## Prerequisites & Quick Start

1. Clone the repository:
   ```sh
   git clone https://github.com/nqnhut92/LinkPeek.git
   cd LinkPeek
   ```
2. **macOS** (requires Xcode with Swift 6):
   - Open [`LinkPeek.xcodeproj`](LinkPeek.xcodeproj), select **LinkPeek → My Mac**, and run, or run core tests from the CLI:
     ```sh
     swift test
     ```
   - Or download `LinkPeek-<version>.zip` from [GitHub Releases](https://github.com/nqnhut92/LinkPeek/releases), move `LinkPeek.app` to **Applications**, and grant Accessibility access in **Settings → General**.
3. **Windows** (requires .NET 10 SDK):
   ```powershell
   dotnet test Windows/LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj
   dotnet run --project Windows/LinkPeek.Windows/LinkPeek.Windows.csproj
   ```
   - Or extract `LinkPeek-Windows-<version>-win-x64.zip` (or `-win-arm64.zip`) from [GitHub Releases](https://github.com/nqnhut92/LinkPeek/releases) and run `LinkPeek.exe`.

## Repository Structure

```text
├── Sources/              # macOS Swift application & core library
├── Tests/                # macOS Swift Testing suite (via Package.swift)
├── LinkPeek.xcodeproj/   # macOS Xcode app project
├── Windows/              # Windows .NET 10 / WPF solution, tests, and desktop checks
├── scripts/              # macOS release packaging script & Python tests
└── docs/                 # Architecture, design, and release documentation
```

## Documentation

- [Technical design (macOS)](docs/TECHNICAL_DESIGN.md) — Architecture, settings, accessibility, approval safeguards, and verification.
- [Release guide](docs/RELEASING.md) — Packaging and publishing macOS and Windows releases.
- [Windows guide](Windows/README.md) — Windows usage, build, checksums, desktop checks, and troubleshooting ([changelog](Windows/CHANGELOG.md)).

## Support

- **International:** [Support on Ko-fi](https://ko-fi.com/nqnhut92)
- **Vietnam (MoMo / VietQR):**

<details>
<summary>Show MoMo / VietQR code (0% fee · scan with any Vietnamese banking app)</summary>

<img src=".github/support-qr.png" alt="MoMo and VietQR code" width="240">

</details>

## License

LinkPeek is licensed under the [MIT License](LICENSE).
