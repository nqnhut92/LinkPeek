// swift-tools-version: 6.0
import PackageDescription

let package = Package(
  name: "LinkPeek", platforms: [.macOS(.v13)],
  products: [.library(name: "LinkPeek", targets: ["LinkPeek"])],
  targets: [
    .target(
      name: "LinkPeek", path: "Sources",
      exclude: [
        "App.swift", "Settings.swift", "HoverCardView.swift",
      ],
      sources: [
        "PRLink.swift", "PanelPlacement.swift", "Credentials.swift", "AzureDevOpsClient.swift",
        "HoverService.swift", "QRGenerator.swift", "AppInfo.swift", "Inspector.swift",
        "PATSecureField.swift",
      ]),
    .testTarget(name: "LinkPeekTests", dependencies: ["LinkPeek"], path: "Tests"),
  ])
