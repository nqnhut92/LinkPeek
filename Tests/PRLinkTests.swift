import AppKit
import CoreImage
import Testing

@testable import LinkPeek

@Test func encodedProject() {
  let link = PRLink(
    "https://dev.azure.com/example/Project%20Name/_git/sample-repository/pullrequest/42?view=files"
  )
  #expect(link?.project == "Project Name")
  #expect(link?.repository == "sample-repository")
  #expect(link?.id == "42")
}
@Test(arguments: [
  "http://dev.azure.com/o/p/_git/r/pullrequest/1",
  "https://dev.azure.com.evil.com/o/p/_git/r/pullrequest/1",
  "https://dev.azure.com/o/p/_git/r/pullrequest/0",
  "https://dev.azure.com/o/p/_git/r/pullrequest/abc",
  "https://dev.azure.com/o/p/_git/r/pullrequest/1/extra",
  "https://user@dev.azure.com/o/p/_git/r/pullrequest/1",
  "https://dev.azure.com/o//_git/r/pullrequest/1", "https://example.com",
]) func rejectsInvalidURL(_ text: String) { #expect(PRLink(text) == nil) }
@Test func acceptsDifferentNames() {
  #expect(PRLink("https://dev.azure.com/another/project/_git/repo/pullrequest/42") != nil)
}

@Test func teamsSchemelessLinkValue() {
  let value = "dev.azure.com/org/Project%20Name/_git/repo/pullrequest/943682"
  #expect(PRLink.accessibilityValue(value, role: "AXLink")?.id == "943682")
  #expect(PRLink.accessibilityValue(value, role: "AXStaticText") == nil)
  #expect(
    PRLink.accessibilityValue("dev.azure.com.evil.com/o/p/_git/r/pullrequest/1", role: "AXLink")
      == nil)
}

@Test(arguments: [Int32(1), Int32(42), Int32(99999)])
func anyForegroundProcessCanBeInspected(_ pid: Int32) {
  #expect(HoverHost.shouldInspect(processIdentifier: pid, ownProcessIdentifier: 123))
}

@Test func skipOwnAndInvalidProcesses() {
  #expect(!HoverHost.shouldInspect(processIdentifier: 123, ownProcessIdentifier: 123))
  #expect(!HoverHost.shouldInspect(processIdentifier: 0, ownProcessIdentifier: 123))
  #expect(!HoverHost.shouldInspect(processIdentifier: -1, ownProcessIdentifier: 123))
}

@Test func browserAndOutlookTargets() {
  let target = "https://dev.azure.com/example/Product/_git/repo/pullrequest/42"
  #expect(PRLink.accessibilityValue(target, role: "AXLink")?.id == "42")
  #expect(PRLink.accessibilityValue("Pull request 42", role: "AXLink") == nil)
  #expect(PRLink("https://example.safelinks.protection.outlook.com/?url=" + target) == nil)
}

@Test func screenshotHoldExpiresAndCancels() {
  var hold = CaptureHold()
  #expect(!hold.isActive(at: 100))
  hold.begin(at: 100)
  #expect(hold.isActive(at: 119.9))
  #expect(!hold.isActive(at: 120))
  hold.begin(at: 200)
  hold.reset()
  #expect(!hold.isActive(at: 201))
}

@Test func screenshotShortcutRemappingAndDisable() {
  let entries: [String: Any] = [
    "30": ["enabled": true, "value": ["type": "standard", "parameters": [52, 40, 0x180000]]],
    "31": ["enabled": false], "184": ["enabled": false],
  ]
  #expect(ScreenshotShortcuts.matches(keyCode: 40, modifiers: 0x180000, entries: entries))
  #expect(!ScreenshotShortcuts.matches(keyCode: 21, modifiers: 0x120000, entries: entries))
  #expect(!ScreenshotShortcuts.matches(keyCode: 23, modifiers: 0x120000, entries: entries))
  #expect(!ScreenshotShortcuts.matches(keyCode: 40, modifiers: 0x120000, entries: entries))
}

@Test func screenshotShortcutDefaultsAndMalformedSettings() {
  #expect(ScreenshotShortcuts.matches(keyCode: 21, modifiers: 0x120000, entries: [:]))
  #expect(ScreenshotShortcuts.matches(keyCode: 21, modifiers: 0x160000, entries: [:]))
  #expect(!ScreenshotShortcuts.matches(keyCode: 20, modifiers: 0x120000, entries: [:]))
  let malformed: [String: Any] = [
    "30": ["enabled": true, "value": ["parameters": []]],
    "31": ["enabled": false], "184": ["enabled": false],
  ]
  #expect(!ScreenshotShortcuts.matches(keyCode: 21, modifiers: 0x120000, entries: malformed))
}

@Test func screenshotCompletionTracksOverlayLifecycle() {
  var hold = CaptureHold()
  hold.begin(at: 100)
  let dismiss1 = hold.shouldDismiss(overlayVisible: false, at: 100.1)
  #expect(!dismiss1)
  let dismiss2 = hold.shouldDismiss(overlayVisible: true, at: 101)
  #expect(!dismiss2)
  let dismiss3 = hold.shouldDismiss(overlayVisible: true, at: 102)
  #expect(!dismiss3)
  let dismiss4 = hold.shouldDismiss(overlayVisible: false, at: 103)
  #expect(!dismiss4)
  let beforeDelay = hold.shouldDismiss(overlayVisible: false, at: 103.99)
  #expect(!beforeDelay)
  let afterDelay = hold.shouldDismiss(overlayVisible: false, at: 104)
  #expect(afterDelay)
  hold.reset()
  let dismiss5 = hold.shouldDismiss(overlayVisible: false, at: 104)
  #expect(!dismiss5)
  hold.begin(at: 200)
  let dismiss6 = hold.shouldDismiss(overlayVisible: false, at: 219)
  #expect(!dismiss6)
  let dismiss7 = hold.shouldDismiss(overlayVisible: false, at: 220)
  #expect(dismiss7)
}

@Test func webTargetsPreserveFullURL() throws {
  let raw = "https://example.com/path?q=a%20b&next=%2Fhello#section"
  let link = try #require(HoverLink(raw))
  #expect(link.url.absoluteString == raw)
  #expect(link.pullRequest == nil)
  #expect(HoverLink("http://localhost:8080/test") != nil)
  #expect(HoverLink.accessibilityValue(raw, role: "AXLink") != nil)
  #expect(HoverLink.accessibilityValue(raw, role: "AXStaticText") == nil)
  #expect(
    HoverLink.accessibilityValue("dev.azure.com/o/p/_git/r/pullrequest/1", role: "AXLink")?
      .pullRequest != nil)
}

@Test(arguments: [
  "", "https://", "example.com", "javascript:alert(1)", "file:///tmp/a", "mailto:a@example.com",
  "https://user:secret@example.com", "Read https://example.com", "https://example.com\n",
])
func rejectNonWebTargets(_ raw: String) {
  #expect(HoverLink(raw) == nil)
}

@Test func webDwellRejectsStaleTargetsAndTracksQuery() throws {
  let first = try #require(HoverLink("https://example.com/?q=1"))
  let second = try #require(HoverLink("https://example.com/?q=2"))
  var gate = DwellGate<URL>()
  let observed = gate.observe(first.url, at: 0, enabled: true)
  #expect(observed)
  let early = gate.start(at: 0.49, delay: 0.5)
  #expect(early == nil)
  let started = gate.start(at: 0.5, delay: 0.5)
  let ticket = try #require(started)
  let repeated = gate.start(at: 1, delay: 0.5)
  #expect(repeated == nil)
  #expect(gate.accepts(ticket, key: first.url))
  let changed = gate.observe(second.url, at: 1, enabled: true)
  #expect(changed)
  #expect(!gate.accepts(ticket, key: first.url))
  let secondEarly = gate.start(at: 1.49, delay: 0.5)
  #expect(secondEarly == nil)
  let off = gate.observe(second.url, at: 2, enabled: false)
  #expect(!off)
  let disabled = gate.start(at: 3, delay: 0.5)
  #expect(disabled == nil)
}

@Test func qrPNGDecodesExactLink() throws {
  let raw = "https://example.com/share?q=hello%20world&lang=vi#section"
  let image = try #require(QRGenerator.makeQRCode(from: raw, size: 600))
  let png = try #require(QRGenerator.pngData(for: image))
  let ciImage = try #require(CIImage(data: png))
  let detector = try #require(
    CIDetector(
      ofType: CIDetectorTypeQRCode, context: nil,
      options: [CIDetectorAccuracy: CIDetectorAccuracyHigh]))
  let decoded = detector.features(in: ciImage).compactMap {
    ($0 as? CIQRCodeFeature)?.messageString
  }
  #expect(decoded == [raw])
  #expect(QRGenerator.makeQRCode(from: "", size: 600) == nil)
  #expect(QRGenerator.makeQRCode(from: raw, size: 0) == nil)
  #expect(QRGenerator.makeQRCode(from: String(repeating: "a", count: 10000), size: 600) == nil)
}

@Test func versionComesFromBuiltBundle() {
  #expect(
    AppInfo.versionLine(info: [
      "CFBundleShortVersionString": "0.1.0", "CFBundleVersion": "20261014.153000",
    ])
      == "Version 0.1.0 (build-20261014.153000)")
  #expect(AppInfo.versionLine(info: nil) == "Version — (build-—)")
}

@Test func renameMigratesOnlyKnownMissingPreferences() {
  let migrated = AppInfo.legacyPreferences(
    [
      "organization": "example", "delayMS": 650.0, "optionHover": true,
      "skipConfirmation": true, "successNotification": false, "unknown": "ignore",
      "token": "ignore",
    ], current: ["delayMS": 400.0, "optionHover": false])
  #expect(migrated["organization"] as? String == "example")
  #expect(migrated["delayMS"] == nil)
  #expect(migrated["optionHover"] == nil)
  #expect(migrated["skipConfirmation"] as? Bool == true)
  #expect(migrated["successNotification"] as? Bool == false)
  #expect(migrated["unknown"] == nil)
  #expect(migrated["token"] == nil)
  #expect(AppInfo.legacyPreferences([:], current: [:]).isEmpty)
}
