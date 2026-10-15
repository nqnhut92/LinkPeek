import AppKit

/// Plain card content; NSPopover owns the native chrome and pointer.
@MainActor
final class HoverCardView: NSView {
  private let content = FlippedCardContent()
  override var isOpaque: Bool { false }
  override var isFlipped: Bool { true }
  enum Theme { case native, graphite, mint }

  init(
    size: CGSize, link: PRLink?, url: URL, qrMode: Bool, qrImage: NSImage?,
    qrStatus: String?, copyAction: Selector, copyLinkAction: Selector, snapshot: PRSnapshot?,
    state: PRDisplayState,
    error: String?, confirmation: ApprovalConfirmation?, busy: Bool,
    target: AnyObject, action: Selector?, cancelAction: Selector,
    skipConfirmation: Bool = false, theme: Theme = .native
  ) {
    super.init(frame: CGRect(origin: .zero, size: size))
    // NSPopover draws one native surface and arrow around this plain content.
    content.frame = bounds.insetBy(dx: 0, dy: 10)
    content.autoresizingMask = [.width, .height]
    addSubview(content)
    let accent: NSColor =
      theme == .mint
      ? .systemGreen : theme == .graphite ? NSColor(calibratedWhite: 0.3, alpha: 1) : .systemBlue
    if qrMode {
      text(
        "LINK QR", x: 20, y: 27, width: size.width - 40, height: 16,
        font: .systemFont(ofSize: 10, weight: .semibold), color: .secondaryLabelColor)
      text(
        url.host ?? "Link", x: 20, y: 51, width: size.width - 40, height: 24,
        font: .systemFont(ofSize: 16, weight: .semibold), color: .labelColor)
      text(
        url.absoluteString, x: 20, y: 78, width: size.width - 40, height: 18,
        font: .systemFont(ofSize: 11), color: .secondaryLabelColor)
      let imageView = NSImageView(
        frame: CGRect(x: (size.width - 180) / 2, y: 104, width: 180, height: 180))
      imageView.image = qrImage
      imageView.imageScaling = .scaleProportionallyUpOrDown
      imageView.wantsLayer = true
      imageView.layer?.backgroundColor = NSColor.white.cgColor
      imageView.setAccessibilityLabel("QR code for " + url.absoluteString)
      content.addSubview(imageView)
      let buttonWidth = (size.width - 48) / 2
      let copyLink = NSButton(title: "Copy Link", target: target, action: copyLinkAction)
      copyLink.bezelStyle = .rounded
      copyLink.frame = CGRect(x: 20, y: 299, width: buttonWidth, height: 32)
      content.addSubview(copyLink)
      let copy = NSButton(title: qrStatus ?? "Copy QR", target: target, action: copyAction)
      copy.bezelStyle = .rounded
      copy.frame = CGRect(x: 28 + buttonWidth, y: 299, width: buttonWidth, height: 32)
      copy.isEnabled = qrImage != nil
      content.addSubview(copy)
      return
    }
    guard let link else { return }
    text(
      "PULL REQUEST  ·  #\(link.id)", x: 20, y: 27, width: size.width - 40, height: 16,
      font: .systemFont(ofSize: 10, weight: .semibold), color: .secondaryLabelColor)
    let pr = confirmation?.snapshot.pr ?? snapshot?.pr
    text(
      pr?.title ?? (state == .loading ? "Loading pull request…" : "\(link.repository)"), x: 20,
      y: 51, width: size.width - 40, height: 46, font: .systemFont(ofSize: 16, weight: .semibold),
      color: .labelColor, wrap: true)
    row(
      icon: "person.crop.circle", value: pr?.createdBy?.displayName ?? "Creator unavailable", y: 105
    )
    row(icon: "folder", value: pr?.repository.name ?? link.repository, y: 129)
    let source = pr?.sourceRefName?.replacingOccurrences(of: "refs/heads/", with: "")
    let destination = pr?.targetRefName?.replacingOccurrences(of: "refs/heads/", with: "")
    let branch =
      source.flatMap { source in destination.map { "\(source) → \($0)" } } ?? "\(link.project)"
    row(icon: "arrow.triangle.branch", value: branch, y: 153)
    let quickWarning = skipConfirmation && confirmation == nil && state.canApprove && !busy
    let stateText =
      confirmation != nil
      ? "Approve this pull request?"
      : state == .loading ? state.rawValue : snapshot?.statusCaption ?? state.rawValue
    text(
      error ?? stateText, x: 20, y: 181, width: size.width - 40, height: 18,
      font: .systemFont(ofSize: 11, weight: confirmation == nil ? .medium : .semibold),
      color: error != nil ? .systemRed : confirmation != nil ? .labelColor : .secondaryLabelColor)
    if quickWarning {
      text(
        "Click Approve to submit immediately.", x: 20, y: 202, width: size.width - 40,
        height: 16, font: .systemFont(ofSize: 11, weight: .medium), color: .systemOrange)
    }
    let buttonY = size.height - 55
    let button = PrimaryActionButton(
      title: confirmation != nil
        ? "Confirm Approve"
        : state.canApprove ? "Approve" : state == .error ? "Retry" : state.rawValue,
      target: target, action: action)
    button.accent = accent
    button.isBordered = false
    button.controlSize = .large
    button.contentTintColor = .white
    button.font = .systemFont(ofSize: 13, weight: .semibold)
    button.image = NSImage(
      systemSymbolName: state.canApprove || confirmation != nil
        ? "checkmark" : state == .error ? "arrow.clockwise" : "info.circle",
      accessibilityDescription: nil)
    button.imagePosition = .imageLeading
    button.frame = CGRect(
      x: 20, y: buttonY - 10, width: confirmation == nil ? size.width - 40 : size.width - 136,
      height: 32
    )
    button.isEnabled = action != nil && !busy
    content.addSubview(button)
    if confirmation != nil {
      let cancel = NSButton(title: "Cancel", target: target, action: cancelAction)
      cancel.bezelStyle = .rounded
      cancel.frame = CGRect(x: size.width - 104, y: buttonY - 10, width: 84, height: 32)
      content.addSubview(cancel)
    }
  }
  required init?(coder: NSCoder) { fatalError("Not supported") }
  private func row(icon: String, value: String, y: CGFloat) {
    let image = NSImageView(frame: CGRect(x: 20, y: y - 10, width: 16, height: 16))
    image.image = NSImage(systemSymbolName: icon, accessibilityDescription: nil)
    image.contentTintColor = .secondaryLabelColor
    content.addSubview(image)
    text(
      value, x: 44, y: y, width: bounds.width - 64, height: 18, font: .systemFont(ofSize: 12),
      color: .secondaryLabelColor)
  }
  private func text(
    _ value: String, x: CGFloat, y: CGFloat, width: CGFloat, height: CGFloat, font: NSFont,
    color: NSColor, wrap: Bool = false
  ) {
    let label =
      wrap ? NSTextField(wrappingLabelWithString: value) : NSTextField(labelWithString: value)
    label.frame = CGRect(x: x, y: y - 10, width: width, height: height)
    label.font = font
    label.textColor = color
    label.toolTip = value
    label.lineBreakMode = wrap ? .byWordWrapping : .byTruncatingTail
    label.maximumNumberOfLines = wrap ? 2 : 1
    content.addSubview(label)
  }
}

@MainActor
private final class FlippedCardContent: NSView {
  override var isFlipped: Bool { true }
  override var isOpaque: Bool { false }
}

/// Retains NSButton actions and accessibility while controlling the accent fill
/// and keeping the SF Symbol adjacent to its label on every supported macOS.
@MainActor
private final class PrimaryActionButton: NSButton {
  var accent: NSColor = .systemGreen
  override var isFlipped: Bool { true }

  override func draw(_ dirtyRect: NSRect) {
    let fill =
      isEnabled
      ? accent.withAlphaComponent(isHighlighted ? 0.8 : 1)
      : NSColor.quaternaryLabelColor
    fill.setFill()
    NSBezierPath(roundedRect: bounds.insetBy(dx: 0, dy: 1), xRadius: 12, yRadius: 12).fill()

    let foreground: NSColor = isEnabled ? .white : .secondaryLabelColor
    let attributes: [NSAttributedString.Key: Any] = [
      .font: font ?? NSFont.systemFont(ofSize: 13, weight: .semibold),
      .foregroundColor: foreground,
    ]
    let titleSize = (title as NSString).size(withAttributes: attributes)
    let iconSize: CGFloat = image == nil ? 0 : 14
    let gap: CGFloat = image == nil ? 0 : 7
    let startX = (bounds.width - titleSize.width - iconSize - gap) / 2
    if let image {
      let symbol =
        image.withSymbolConfiguration(
          NSImage.SymbolConfiguration(paletteColors: [foreground])) ?? image
      symbol.draw(
        in: CGRect(x: startX, y: (bounds.height - iconSize) / 2, width: iconSize, height: iconSize),
        from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
    }
    (title as NSString).draw(
      at: CGPoint(x: startX + iconSize + gap, y: (bounds.height - titleSize.height) / 2),
      withAttributes: attributes)
  }
}
