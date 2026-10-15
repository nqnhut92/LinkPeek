import AppKit
import CoreImage
import CoreImage.CIFilterBuiltins

/// Offline Core Image generator with an integer
/// module scale and four-module white quiet zone for reliable scanning.
enum QRGenerator {
  static func makeQRCode(from string: String, size: CGFloat) -> NSImage? {
    guard !string.isEmpty, size.isFinite, size > 0 else { return nil }
    let filter = CIFilter.qrCodeGenerator()
    filter.message = Data(string.utf8)
    filter.correctionLevel = "M"
    guard let output = filter.outputImage else { return nil }
    let modules = output.extent.width + 8
    let scale = max(1, floor(size / modules))
    let extent = CGRect(x: 0, y: 0, width: modules, height: modules)
    let code = output.transformed(by: CGAffineTransform(translationX: 4, y: 4))
    let white = CIImage(color: .white).cropped(to: extent)
    let padded = code.composited(over: white).cropped(to: extent)
      .transformed(by: CGAffineTransform(scaleX: scale, y: scale))
    guard let bitmap = CIContext().createCGImage(padded, from: padded.extent) else { return nil }
    return NSImage(cgImage: bitmap, size: padded.extent.size)
  }
  static func pngData(for image: NSImage) -> Data? {
    guard let tiff = image.tiffRepresentation,
      let rep = NSBitmapImageRep(data: tiff)
    else { return nil }
    return rep.representation(using: .png, properties: [:])
  }
}
