import Foundation

struct PRLink: Equatable, Sendable {
  let url: URL
  let organization: String
  let project: String
  let repository: String
  let id: String

  // Teams AXLink values can omit the scheme; limit normalization to the exact host.
  static func accessibilityValue(_ text: String, role: String) -> PRLink? {
    if let link = PRLink(text) { return link }
    guard role == "AXLink", text.hasPrefix("dev.azure.com/") else { return nil }
    return PRLink("https://" + text)
  }

  init?(_ text: String) {
    guard let url = URL(string: text), url.scheme?.lowercased() == "https",
      url.host?.lowercased() == "dev.azure.com", url.user == nil, url.password == nil,
      url.port == nil || url.port == 443
    else { return nil }
    let parts = url.path.split(separator: "/", omittingEmptySubsequences: false)
    guard parts.count == 7, parts[0].isEmpty, parts[3] == "_git",
      parts[5] == "pullrequest", !parts[1].isEmpty, !parts[2].isEmpty,
      !parts[4].isEmpty, !parts[6].isEmpty,
      parts[6].allSatisfy({ $0.isASCII && $0.isNumber }),
      let number = UInt64(parts[6]), number > 0
    else { return nil }
    self.url = url
    organization = String(parts[1])
    project = String(parts[2])
    repository = String(parts[4])
    id = String(parts[6])
  }
}

/// Inspect any foreground process except our own panels and Settings.
enum HoverHost {
  static func shouldInspect(processIdentifier: Int32, ownProcessIdentifier: Int32) -> Bool {
    processIdentifier > 0 && processIdentifier != ownProcessIdentifier
  }
}

/// Bounded visibility hold for the system's screenshot selection overlay.
struct CaptureHold {
  private var until: TimeInterval = 0
  private var sawOverlay = false
  private var dismissAfter: TimeInterval?
  var hasStarted: Bool { until > 0 }
  mutating func begin(at time: TimeInterval) {
    until = time + 20
    sawOverlay = false
    dismissAfter = nil
  }
  mutating func shouldDismiss(overlayVisible: Bool, at time: TimeInterval) -> Bool {
    guard hasStarted else { return false }
    if time >= until { return true }
    if overlayVisible { sawOverlay = true }
    if sawOverlay && !overlayVisible && dismissAfter == nil { dismissAfter = time + 1 }
    return dismissAfter.map { time >= $0 } ?? false
  }
  func isActive(at time: TimeInterval) -> Bool { time < until }
  mutating func reset() {
    until = 0
    sawOverlay = false
    dismissAfter = nil
  }
}

/// macOS symbolic hotkey preferences are read-only; missing entries use OS defaults.
/// IDs 30/31 select a region (file/clipboard), 184 opens screenshot options.
struct ScreenshotShortcuts {
  private static let modifierMask: UInt = 0x1E0000
  static func matches(keyCode: UInt16, modifiers: UInt, entries: [String: Any]) -> Bool {
    for (id, defaultKey, defaultFlags) in [
      ("30", 21, 0x120000), ("31", 21, 0x160000), ("184", 23, 0x120000),
    ] {
      var key = defaultKey
      var flags = defaultFlags
      if let entry = entries[id] as? [String: Any] {
        guard entry["enabled"] as? Bool == true,
          let value = entry["value"] as? [String: Any], value["type"] as? String == "standard",
          let parameters = value["parameters"] as? [NSNumber], parameters.count == 3
        else { continue }
        key = parameters[1].intValue
        flags = parameters[2].intValue
      }
      if Int(keyCode) == key, modifiers & modifierMask == UInt(flags) & modifierMask {
        return true
      }
    }
    return false
  }
}

/// A complete web target exposed by Accessibility; never extract URLs from prose.
struct HoverLink: Equatable, Sendable {
  let url: URL
  var pullRequest: PRLink? { PRLink(url.absoluteString) }
  init?(_ text: String) {
    guard !text.isEmpty, !text.contains(where: { $0.isWhitespace || $0.isNewline }),
      let url = URL(string: text), let scheme = url.scheme?.lowercased(),
      ["http", "https"].contains(scheme), let host = url.host, !host.isEmpty,
      url.user == nil, url.password == nil
    else { return nil }
    self.url = url
  }
  static func accessibilityValue(_ text: String, role: String) -> Self? {
    guard role == "AXLink" else { return nil }
    if let link = Self(text) { return link }
    // Preserve the existing Teams normalization, bounded to its known host.
    return PRLink.accessibilityValue(text, role: role).flatMap { Self($0.url.absoluteString) }
  }
}
