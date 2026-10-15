import Foundation

/// Bundle-backed version display and the bounded preference migration for rename.
enum AppInfo {
  static let repositoryURL = URL(string: "https://github.com/nqnhut92/LinkPeek")!
  static func versionLine(info: [String: Any]?) -> String {
    let version = info?["CFBundleShortVersionString"] as? String ?? "—"
    let build = info?["CFBundleVersion"] as? String ?? "—"
    return "Version \(version) (build-\(build))"
  }
  static func legacyPreferences(_ legacy: [String: Any], current: [String: Any]) -> [String: Any] {
    var result: [String: Any] = [:]
    for key in [
      "organization", "optionHover", "delayMS", "skipConfirmation", "successNotification",
    ] {
      if current[key] == nil, let value = legacy[key] { result[key] = value }
    }
    return result
  }
}
