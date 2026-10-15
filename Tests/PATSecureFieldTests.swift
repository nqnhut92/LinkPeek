import AppKit
import Testing

@testable import LinkPeek

@Test @MainActor func savedPATMaskKeepsNativeGeometryButUsesPlaceholderColor() {
  _ = NSApplication.shared
  func render(saved: Bool) -> (pixels: [CGFloat], width: Int) {
    let field = PATSecureField(frame: NSRect(x: 0, y: 0, width: 200, height: 28))
    field.appearance = NSAppearance(named: .aqua)
    field.font = .systemFont(ofSize: 18, weight: .medium)
    field.isBezeled = true
    field.bezelStyle = .roundedBezel
    field.stringValue = saved ? "" : "00000000"
    field.showsSavedMask = saved
    let rep = field.bitmapImageRepForCachingDisplay(in: field.bounds)!
    field.effectiveAppearance.performAsCurrentDrawingAppearance {
      field.cacheDisplay(in: field.bounds, to: rep)
    }
    #expect(field.stringValue == (saved ? "" : "00000000"))
    // Exclude the border and compare glyph geometry independently of ink color.
    let pixels = (4..<rep.pixelsHigh - 4).flatMap { y in
      (4..<rep.pixelsWide - 4).map { x -> CGFloat in
        guard let color = rep.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB),
          color.alphaComponent > 0.9
        else { return 1 }
        return (color.redComponent + color.greenComponent + color.blueComponent) / 3
      }
    }
    return (pixels, rep.pixelsWide - 8)
  }
  let saved = render(saved: true)
  let entered = render(saved: false)
  let savedInk = saved.pixels.min()!
  let enteredInk = entered.pixels.min()!
  #expect(savedInk > enteredInk + 0.2)
  #expect(savedInk < 0.8)
  func bounds(_ pixels: [CGFloat], width: Int, ink: CGFloat) -> CGRect {
    let indices = pixels.indices.filter { pixels[$0] < (1 + ink) / 2 }
    let xs = indices.map { $0 % width }
    let ys = indices.map { $0 / width }
    return CGRect(
      x: xs.min()!, y: ys.min()!, width: xs.max()! - xs.min()! + 1,
      height: ys.max()! - ys.min()! + 1)
  }
  let savedBounds = bounds(saved.pixels, width: saved.width, ink: savedInk)
  let enteredBounds = bounds(entered.pixels, width: entered.width, ink: enteredInk)
  // Different ink colors can move the antialiased edge by one bitmap pixel.
  for (savedEdge, enteredEdge) in zip(
    [savedBounds.minX, savedBounds.maxX, savedBounds.minY, savedBounds.maxY],
    [enteredBounds.minX, enteredBounds.maxX, enteredBounds.minY, enteredBounds.maxY]
  ) {
    #expect(abs(savedEdge - enteredEdge) <= 1)
  }
  let savedShape = saved.pixels.map { $0 < (1 + savedInk) / 2 }
  let enteredShape = entered.pixels.map { $0 < (1 + enteredInk) / 2 }
  let differingPixels = zip(savedShape, enteredShape).filter { $0 != $1 }.count
  let inkPixels = enteredShape.filter { $0 }.count
  #expect(differingPixels * 10 <= inkPixels)
}
