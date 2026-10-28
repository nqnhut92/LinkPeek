import Foundation

/// Hover dwell bounds and the one-time move from the 0.1.x defaults (500 ms, 300–1000 ms).
enum HoverDelay {
  static let range: ClosedRange<Double> = 500...1500
  static let standard: Double = 800
  static let currentVersion = 2
  /// Version 1 (unversioned) stored 500 ms for users who never touched the slider.
  static func resolve(stored: Double, version: Int) -> Double {
    if stored == 0 || (version < currentVersion && stored == 500) { return standard }
    return min(max(stored, range.lowerBound), range.upperBound)
  }
}

/// Case-insensitive `host/path` glob. `*` matches any run of characters; a pattern without
/// a slash matches the host and every path on it.
enum LinkPattern {
  static func normalize(_ raw: String) -> String? {
    var text = raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    if let range = text.range(of: "://") { text = String(text[range.upperBound...]) }
    while text.hasSuffix("/") { text.removeLast() }
    guard !text.isEmpty, !text.contains(where: \.isWhitespace),
      text.contains(where: { $0 != "*" && $0 != "/" })
    else { return nil }
    return text.contains("/") ? text : text + "/*"
  }

  static func matches(_ pattern: String, url: URL) -> Bool {
    guard let host = url.host?.lowercased(), let pattern = normalize(pattern) else { return false }
    var path = url.path.lowercased()
    while path.hasSuffix("/") { path.removeLast() }
    return glob(Array(pattern), Array(host + (path.isEmpty ? "/" : path)))
  }

  private static func glob(_ pattern: [Character], _ text: [Character]) -> Bool {
    var p = 0
    var t = 0
    var star: Int?
    var resume = 0
    while t < text.count {
      if p < pattern.count, pattern[p] == "*" {
        star = p
        resume = t
        p += 1
      } else if p < pattern.count, pattern[p] == text[t] {
        p += 1
        t += 1
      } else if let star {
        resume += 1
        t = resume
        p = star + 1
      } else {
        return false
      }
    }
    while p < pattern.count, pattern[p] == "*" { p += 1 }
    return p == pattern.count
  }
}

/// Decides which non-PR links may open a QR card.
struct LinkFilter: Equatable, Sendable {
  static let grabPattern = "app.grab.com/s/*"
  var qrEnabled = true
  var grabEnabled = true
  var patterns: [String] = []
  var anyOtherLink = false

  func allowsQR(_ url: URL) -> Bool {
    guard qrEnabled else { return false }
    if anyOtherLink { return true }
    if grabEnabled, LinkPattern.matches(Self.grabPattern, url: url) { return true }
    return patterns.contains { LinkPattern.matches($0, url: url) }
  }
}
