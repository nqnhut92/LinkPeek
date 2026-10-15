import CoreGraphics
import Foundation
import Testing

@testable import LinkPeek

@Test func panelAvoidsLinkAndClampsToSecondaryScreen() throws {
  let visible = CGRect(x: -1920, y: -300, width: 1920, height: 1000)
  let link = CGRect(x: -200, y: -280, width: 180, height: 20)
  let result = try #require(
    PanelPlacement.frame(
      link: link, cursor: CGPoint(x: -30, y: -270), visible: visible,
      size: CGSize(width: 160, height: 36)))
  #expect(visible.contains(result))
  #expect(!result.intersects(link))
  #expect(result.minY > link.maxY)
}
@Test func noPlacementWhenLinkFillsScreen() {
  let visible = CGRect(x: 0, y: 0, width: 400, height: 100)
  #expect(
    PanelPlacement.frame(
      link: visible, cursor: .zero, visible: visible, size: CGSize(width: 160, height: 36)) == nil)
}
