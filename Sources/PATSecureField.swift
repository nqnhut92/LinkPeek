import AppKit

@MainActor
final class PATSecureField: NSSecureTextField {
  var showsSavedMask = false
  private let savedMask = NSSecureTextFieldCell(textCell: "00000000")

  override func draw(_ dirtyRect: NSRect) {
    super.draw(dirtyRect)
    guard showsSavedMask, stringValue.isEmpty, let cell else { return }
    savedMask.font = font
    savedMask.textColor = .secondaryLabelColor
    savedMask.isBordered = false
    savedMask.isBezeled = false
    savedMask.drawInterior(withFrame: cell.drawingRect(forBounds: bounds), in: self)
  }
}
