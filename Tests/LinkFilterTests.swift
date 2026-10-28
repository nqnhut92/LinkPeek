import Foundation
import Testing

@testable import LinkPeek

private func url(_ text: String) -> URL { URL(string: text)! }

@Test func grabShortLinkIsAllowedByDefault() {
  let filter = LinkFilter()
  #expect(filter.allowsQR(url("https://app.grab.com/s/QIe9bSIw")))
  #expect(!filter.allowsQR(url("https://app.grab.com/")))
  #expect(!filter.allowsQR(url("https://github.com/nqnhut92/LinkPeek")))
}

@Test func masterSwitchAndGrabSwitchDisableQR() {
  var filter = LinkFilter(patterns: ["github.com/*"], anyOtherLink: true)
  filter.qrEnabled = false
  #expect(!filter.allowsQR(url("https://app.grab.com/s/abc")))
  filter.qrEnabled = true
  filter.grabEnabled = false
  filter.anyOtherLink = false
  #expect(!filter.allowsQR(url("https://app.grab.com/s/abc")))
  #expect(filter.allowsQR(url("https://github.com/a/b")))
}

@Test func anyOtherLinkAllowsEverything() {
  let filter = LinkFilter(anyOtherLink: true)
  #expect(filter.allowsQR(url("https://example.com/x?y=1")))
}

@Test func patternsAreCaseInsensitiveAndIgnoreSchemeQueryAndPort() {
  #expect(LinkPattern.matches("GitHub.com/Org/*", url: url("http://github.com/org/repo?tab=1")))
  #expect(LinkPattern.matches("*.notion.so/*", url: url("https://team.notion.so/page")))
  #expect(!LinkPattern.matches("*.notion.so/*", url: url("https://notion.so/page")))
  #expect(LinkPattern.matches("example.com/a", url: url("https://example.com:8080/a/")))
}

@Test func hostOnlyPatternCoversEveryPath() {
  #expect(LinkPattern.matches("example.com", url: url("https://example.com")))
  #expect(LinkPattern.matches("example.com", url: url("https://example.com/a/b")))
  #expect(!LinkPattern.matches("example.com", url: url("https://notexample.com/a")))
}

@Test func normalizeRejectsEmptyWhitespaceAndBareWildcards() {
  #expect(LinkPattern.normalize("  ") == nil)
  #expect(LinkPattern.normalize("*") == nil)
  #expect(LinkPattern.normalize("**/") == nil)
  #expect(LinkPattern.normalize("a b/c") == nil)
  #expect(LinkPattern.normalize("https://GitHub.com/Org/") == "github.com/org")
  #expect(LinkPattern.normalize("github.com") == "github.com/*")
}

@Test func delayMigrationMovesOldDefaultOnceAndClamps() {
  #expect(HoverDelay.resolve(stored: 0, version: 0) == 800)
  #expect(HoverDelay.resolve(stored: 500, version: 0) == 800)
  #expect(HoverDelay.resolve(stored: 500, version: 2) == 500)
  #expect(HoverDelay.resolve(stored: 350, version: 0) == 500)
  #expect(HoverDelay.resolve(stored: 1000, version: 0) == 1000)
  #expect(HoverDelay.resolve(stored: 9000, version: 2) == 1500)
}
