# LinkPeek

LinkPeek previews hovered web links as offline QR codes and Azure DevOps pull requests from the macOS menu bar or Windows system tray.

## Features

- **Offline QR previews** — Displays a QR code on link hover (Grab links and custom patterns by default, or all links) with **Copy Link** and **Copy QR** actions.
- **Azure DevOps PR previews** — Shows PR status and supports approval with explicit confirmation by default and server-side verification before voting.
- **Configurable hover** — Adjust dwell delay, optional modifier-key trigger, and launch at login / sign-in.
- **Native UI** — Built natively for macOS (SwiftUI / AppKit, Liquid Glass on macOS 26+) and Windows (WPF / Fluent Design).

## Requirements

| Platform | Runtime | Build from source |
| --- | --- | --- |
| macOS | macOS 13+ | Xcode with Swift 6 |
| Windows | Windows 10 22H2 / Windows 11 (x64 or ARM64) | .NET 10 SDK |

## Install & start

Download the latest release archive from [GitHub Releases](https://github.com/nqnhut92/LinkPeek/releases) (not the automatic source-code ZIP):

- **macOS (`LinkPeek-<version>.zip`):** Unzip, move `LinkPeek.app` to **Applications**, and open it (use **System Settings → Privacy & Security → Open Anyway** if blocked on first launch). Grant Accessibility access in **Settings → General**.
- **Windows (`LinkPeek-Windows-<version>-win-x64.zip` or `-win-arm64.zip`):** Extract the full ZIP to a folder and run `LinkPeek.exe` (self-contained, no installer required).
- **Build from source:**
  - **macOS:** Open [`LinkPeek.xcodeproj`](LinkPeek.xcodeproj), select **LinkPeek → My Mac**, and Run.
  - **Windows:** See [Windows/README.md](Windows/README.md).

## Documentation

- [Technical design (macOS)](docs/TECHNICAL_DESIGN.md) — Architecture, settings, accessibility, approval safeguards, and verification.
- [Windows guide](Windows/README.md) — Windows usage, build, checksums, desktop checks, and troubleshooting ([changelog](Windows/CHANGELOG.md)).
- [Release guide](docs/RELEASING.md) — Packaging and publishing macOS and Windows releases.

## Support

- **International:** [Support on Ko-fi](https://ko-fi.com/nqnhut92)
- **Vietnam (MoMo / VietQR):**

<details>
<summary>Show MoMo / VietQR code (0% fee · scan with any Vietnamese banking app)</summary>

<img src=".github/support-qr.png" alt="MoMo and VietQR code" width="240">

</details>

## License

LinkPeek is licensed under the [MIT License](LICENSE).
