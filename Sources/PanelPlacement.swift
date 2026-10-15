import CoreGraphics
import Foundation

struct PanelPlacement {
  static func frame(link: CGRect, cursor: CGPoint, visible: CGRect, size: CGSize) -> CGRect? {
    guard size.width <= visible.width, size.height <= visible.height else { return nil }
    let x = min(max(cursor.x, visible.minX), visible.maxX - size.width)
    for y in [link.minY - size.height - 6, link.maxY + 6] {
      let candidate = CGRect(
        x: x, y: min(max(y, visible.minY), visible.maxY - size.height), width: size.width,
        height: size.height)
      if !candidate.intersects(link) { return candidate }
    }
    return nil
  }
}
