import AppKit
import ApplicationServices

struct Inspection: Sendable {
  var link: HoverLink?
  var frame: CGRect?
  var diagnostics: [String] = []
}

// System-wide hit testing can enter AppKit's in-process accessibility code,
// which asserts the main queue. Keep AX objects on that queue; return values only.
final class Inspector: Sendable {
  private let inspection: @MainActor @Sendable (CGPoint, pid_t, Bool) -> Inspection

  init(
    inspection: @escaping @MainActor @Sendable (CGPoint, pid_t, Bool) -> Inspection = {
      Inspector.read(point: $0, pid: $1, debug: $2)
    }
  ) {
    self.inspection = inspection
  }

  func inspect(
    point: CGPoint, pid: pid_t, debug: Bool, completion: @escaping @Sendable (Inspection) -> Void
  ) {
    DispatchQueue.main.async {
      completion(self.inspection(point, pid, debug))
    }
  }

  /// Chromium builds its web accessibility tree only after a client sets AXEnhancedUserInterface;
  /// Electron uses AXManualAccessibility. Ask once per process; unsupported attributes are ignored.
  @MainActor private static var webAccessibilityRequested: Set<pid_t> = []

  @MainActor
  private static func requestWebAccessibility(pid: pid_t) {
    guard webAccessibilityRequested.insert(pid).inserted else { return }
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 0.15)
    for attribute in ["AXEnhancedUserInterface", "AXManualAccessibility"] {
      _ = AXUIElementSetAttributeValue(app, attribute as CFString, kCFBooleanTrue)
    }
  }

  @MainActor
  private static func read(point: CGPoint, pid: pid_t, debug: Bool) -> Inspection {
    dispatchPrecondition(condition: .onQueue(.main))
    requestWebAccessibility(pid: pid)
    let system = AXUIElementCreateSystemWide()
    AXUIElementSetMessagingTimeout(system, 0.15)
    var hit: AXUIElement?
    let error = AXUIElementCopyElementAtPosition(system, Float(point.x), Float(point.y), &hit)
    guard error == .success, let hit else {
      return Inspection(diagnostics: ["Hit test failed: AXError \(error.rawValue)"])
    }
    var owner: pid_t = 0
    AXUIElementGetPid(hit, &owner)
    guard owner == pid else {
      return Inspection(diagnostics: ["Hit belongs to another process"])
    }
    var result = Inspection()
    var nodes: [(AXUIElement, Int)] = [(hit, 0)]
    var visited: [AXUIElement] = []
    let deadline = Date().addingTimeInterval(0.35)
    while !nodes.isEmpty, visited.count < 24, Date() < deadline {
      let (node, depth) = nodes.removeFirst()
      if visited.contains(where: { CFEqual($0, node) }) { continue }
      visited.append(node)
      let frame = Self.frame(node)
      let role = Self.attribute(node, kAXRoleAttribute) as? String ?? "missing"
      let subrole = Self.attribute(node, kAXSubroleAttribute) as? String ?? "missing"
      let urlValue = Self.attribute(node, kAXURLAttribute)
      let value = Self.attribute(node, kAXValueAttribute)
      let urlText = (urlValue as? URL)?.absoluteString ?? (urlValue as? String)
      // Only accept a complete URL value, never scrape message bodies.
      let candidate = Self.linkCandidate(urlText: urlText, value: value as? String, role: role)
      if debug {
        result.diagnostics.append(
          "depth=\(depth) role=\(role) subrole=\(subrole) value=\(Self.redacted(value)) URL=\(Self.redacted(urlValue)) frame=\(String(describing: frame))"
        )
      }
      // Nearby descendants must actually contain the cursor. An ancestor URL
      // is accepted only with a small frame to avoid whole-message false positives.
      if let candidate, let frame, frame.contains(point), frame.width <= 1600, frame.height <= 100 {
        result.link = candidate
        result.frame = frame
        break
      }
      if depth < 3 {
        if let parent = Self.attribute(node, kAXParentAttribute),
          CFGetTypeID(parent) == AXUIElementGetTypeID()
        {
          nodes.append((unsafeDowncast(parent, to: AXUIElement.self), depth + 1))
        }
        var children: CFArray?
        if AXUIElementCopyAttributeValues(node, kAXChildrenAttribute as CFString, 0, 8, &children)
          == .success,
          let children = children as? [AXUIElement]
        {
          nodes += children.filter { Self.frame($0)?.contains(point) == true }.map {
            ($0, depth + 1)
          }
        }
      }
    }
    if result.link == nil {
      result.diagnostics.append(
        "No web URL in bounded AX inspection (\(visited.count) nodes); The foreground app may not expose the target URL."
      )
    }
    return result
  }

  /// AXImage's URL is its image source, not a navigation target. Keep traversing
  /// so an enclosing AXLink can still supply a linked image's destination.
  static func linkCandidate(urlText: String?, value: String?, role: String) -> HoverLink? {
    guard role != kAXImageRole as String else { return nil }
    return urlText.flatMap(HoverLink.init)
      ?? value.flatMap { HoverLink.accessibilityValue($0, role: role) }
  }

  private static func attribute(_ node: AXUIElement, _ name: String) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(node, name as CFString, &value) == .success ? value : nil
  }

  private static func frame(_ node: AXUIElement) -> CGRect? {
    guard let position = attribute(node, kAXPositionAttribute),
      let size = attribute(node, kAXSizeAttribute),
      CFGetTypeID(position) == AXValueGetTypeID(), CFGetTypeID(size) == AXValueGetTypeID()
    else { return nil }
    var point = CGPoint.zero
    var dimensions = CGSize.zero
    guard AXValueGetValue(unsafeDowncast(position, to: AXValue.self), .cgPoint, &point),
      AXValueGetValue(unsafeDowncast(size, to: AXValue.self), .cgSize, &dimensions)
    else { return nil }
    return CGRect(origin: point, size: dimensions)
  }

  private static func redacted(_ value: CFTypeRef?) -> String {
    guard let value else { return "missing/unsupported" }
    let text = (value as? URL)?.absoluteString ?? (value as? String)
    if let text, let pr = PRLink(text) {
      return "ADO PR repository=\(pr.repository) id=\(pr.id) (organization/project/query redacted)"
    }
    return "<redacted type=\(CFGetTypeID(value)) length=\(text?.count ?? 0)>"
  }
}
