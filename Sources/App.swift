import AppKit
@preconcurrency import ApplicationServices
import OSLog
import SwiftUI
import UserNotifications

@main
struct LinkPeekApp: App {
  @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
  var body: some Scene { Settings { EmptyView() } }
}

final class HoverPanel: NSPanel {
  override var canBecomeKey: Bool { false }
  override var canBecomeMain: Bool { false }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSPopoverDelegate,
  NSMenuDelegate
{
  private var status: NSStatusItem!
  private var timer: Timer?
  private var captureHold = CaptureHold()
  private var screenshotMonitor: Any?
  private var captureDismissedAnchor: CGRect?
  private var debug = CommandLine.arguments.contains("--debug")
  private var lastTrust: Bool?
  private var busy = false
  private var lastPoint: CGPoint?
  private var lastPID: pid_t?
  private var lastInspection = Date.distantPast
  private var lastSeen = Date.distantPast
  private let inspector = Inspector()
  private let logger = Logger(subsystem: "com.linkpeek.app", category: "Hover")
  private var targetLink: HoverLink?
  private var current: PRLink? { targetLink?.pullRequest }
  private var showingQR = false
  private var qrImage: NSImage?
  private var qrStatus: String?
  private var resultUntil = Date.distantPast
  private let preferences = Preferences.shared
  private let client = AzureDevOpsClient()
  private lazy var service = HoverService(client: client)
  private var gate = DwellGate<URL>()
  private var hoverTask: Task<Void, Never>?
  private var actionTask: Task<Void, Never>?
  private var displayedSnapshot: PRSnapshot?
  private var displayState: PRDisplayState = .loading
  private var errorMessage: String?
  private var confirmation: ApprovalConfirmation?
  private var actionBusy = false
  private var anchor: CGRect?
  private var settingsWindow: NSWindow?
  private var statusMenuTracking = false
  private var settingsRequestedFromMenu = false
  private var uiEpoch = UUID()
  private var closingPopover = false
  private let popover = NSPopover()
  private let popoverController = NSViewController()
  // A transparent, mouse-ignoring anchor bridges the external app’s AX rectangle
  // to the NSView anchor required by NSPopover. It never draws card chrome.
  private let anchorPanel = HoverPanel(
    contentRect: NSRect(x: 0, y: 0, width: 160, height: 36),
    styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)

  func applicationDidFinishLaunching(_ notification: Notification) {
    updateApplicationIcon()
    NSApp.setActivationPolicy(.accessory)
    status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    status.button?.image = NSImage(systemSymbolName: "link", accessibilityDescription: "LinkPeek")
    status.button?.image?.isTemplate = true
    status.button?.toolTip = "LinkPeek"
    let menu = NSMenu()
    menu.delegate = self
    for (title, action) in [
      ("Settings…", #selector(openSettings)),
      ("Quit LinkPeek", #selector(quit)),
    ] {
      let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
      item.target = self
      menu.addItem(item)
    }
    status.menu = menu
    anchorPanel.level = .floating
    anchorPanel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    anchorPanel.isOpaque = false
    anchorPanel.backgroundColor = .clear
    anchorPanel.hasShadow = false
    anchorPanel.ignoresMouseEvents = true
    anchorPanel.hidesOnDeactivate = false
    anchorPanel.isReleasedWhenClosed = false
    anchorPanel.contentView = NSView()
    if #available(macOS 26.0, *) { popover.hasFullSizeContent = true }
    popover.behavior = .applicationDefined
    popover.animates = false
    popover.delegate = self
    popover.contentViewController = popoverController
    timer = Timer.scheduledTimer(withTimeInterval: 0.12, repeats: true) { [weak self] _ in
      Task { @MainActor in self?.tick() }
    }
    screenshotMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
      let keyCode = event.keyCode
      let modifiers = event.modifierFlags.rawValue
      Task { @MainActor in
        guard let self else { return }
        if keyCode == 53, self.captureHold.hasStarted {
          self.finishCapture()
          return
        }
        guard self.popover.isShown else { return }
        // Refresh the system preference domain so remaps do not require app restart.
        let domain = "com.apple.symbolichotkeys" as CFString
        CFPreferencesAppSynchronize(domain)
        let entries =
          CFPreferencesCopyAppValue("AppleSymbolicHotKeys" as CFString, domain)
          as? [String: Any] ?? [:]
        if ScreenshotShortcuts.matches(keyCode: keyCode, modifiers: modifiers, entries: entries) {
          self.captureHold.begin(at: ProcessInfo.processInfo.systemUptime)
        }
      }
    }
    preferences.changed = { [weak self] in
      guard let self else { return }
      self.hide()
      Task { await self.service.invalidate() }
    }
    if CommandLine.arguments.contains("--settings") { openSettings() }
    if !AXIsProcessTrusted() { requestAccessibility() }
    logger.notice("LinkPeek started; permission=\(AXIsProcessTrusted())")
  }

  private func requestAccessibility() {
    let options =
      [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
    _ = AXIsProcessTrustedWithOptions(options)
  }
  @objc private func permission() {
    requestAccessibility()
    NSWorkspace.shared.open(
      URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
  }
  @objc private func quit() { NSApp.terminate(nil) }
  private func hide() {
    captureHold.reset()
    closingPopover = true
    if popover.isShown { popover.close() }
    closingPopover = false
    anchorPanel.orderOut(nil)
    hoverTask?.cancel()
    hoverTask = nil
    actionTask?.cancel()
    actionTask = nil
    if let confirmation { Task { await service.discard(confirmation) } }
    confirmation = nil
    actionBusy = false
    targetLink = nil
    showingQR = false
    qrImage = nil
    qrStatus = nil
    anchor = nil
    displayedSnapshot = nil
    gate.reset()
    uiEpoch = UUID()
  }

  private func updateApplicationIcon() {
    if let iconURL = Bundle.main.url(forResource: "AppIcon", withExtension: "png"),
      let icon = NSImage(contentsOf: iconURL)
    {
      NSApp.applicationIconImage = icon
    }
  }

  @objc private func openSettings() {
    if statusMenuTracking {
      settingsRequestedFromMenu = true
      status.menu?.cancelTracking()
      return
    }
    hide()
    if settingsWindow == nil {
      let window = NSWindow(
        contentRect: NSRect(origin: .zero, size: PreferencesView.windowSize),
        styleMask: [.titled, .closable],
        backing: .buffered, defer: false)
      window.title = "LinkPeek Settings"
      window.delegate = self
      window.isReleasedWhenClosed = false
      let hosting = NSHostingView(
        rootView: PreferencesView(
          preferences: preferences, client: client,
          grantAccessibility: { [weak self] in self?.permission() }))
      hosting.sizingOptions = []
      window.contentView = hosting
      window.center()
      settingsWindow = window
    }
    // Publish a visible, eligible window before requesting app activation.
    // The activation notification then finishes the key/main transition.
    DispatchQueue.main.async { [weak self] in
      guard let self, let settingsWindow = self.settingsWindow else { return }
      if NSApp.activationPolicy() != .regular { NSApp.setActivationPolicy(.regular) }
      self.updateApplicationIcon()
      if settingsWindow.isMiniaturized { settingsWindow.deminiaturize(nil) }
      settingsWindow.orderFront(nil)
      if NSApp.isActive {
        self.focusSettings()
      } else {
        NSApp.activate(ignoringOtherApps: true)
      }
    }
  }

  func menuWillOpen(_ menu: NSMenu) {
    statusMenuTracking = true
  }

  func menuDidClose(_ menu: NSMenu) {
    statusMenuTracking = false
    guard settingsRequestedFromMenu else { return }
    settingsRequestedFromMenu = false
    // Wait for the menu's close callback to return before activation.
    DispatchQueue.main.async { [weak self] in self?.openSettings() }
  }

  func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool
  {
    openSettings()
    return false
  }

  func applicationDidBecomeActive(_ notification: Notification) {
    focusSettings()
  }

  private func focusSettings() {
    guard let settingsWindow, settingsWindow.isVisible else { return }
    settingsWindow.makeMain()
    (settingsWindow.attachedSheet ?? settingsWindow).makeKeyAndOrderFront(nil)
  }

  func windowWillClose(_ notification: Notification) {
    guard let window = notification.object as? NSWindow, window === settingsWindow else { return }
    NSApp.setActivationPolicy(.accessory)
    settingsWindow = nil
  }

  private func finishCapture() {
    captureDismissedAnchor = anchor
    hide()
  }

  private func screenshotOverlayVisible() -> Bool {
    // Owner PID/bundle metadata does not require reading screen pixels or titles.
    // Wait until the system overlay has appeared before treating its exit as completion.
    let pids = Set(
      NSWorkspace.shared.runningApplications.filter {
        $0.bundleIdentifier == "com.apple.screencaptureui"
      }.map(\.processIdentifier))
    guard !pids.isEmpty,
      let windows = CGWindowListCopyWindowInfo(
        [.optionOnScreenOnly, .excludeDesktopElements],
        kCGNullWindowID) as? [[String: Any]]
    else { return false }
    return windows.contains {
      guard let owner = $0[kCGWindowOwnerPID as String] as? NSNumber else { return false }
      return pids.contains(owner.int32Value)
    }
  }

  private func tick() {
    let trusted = AXIsProcessTrusted()
    if lastTrust != trusted {
      lastTrust = trusted
      logger.notice("Accessibility permission=\(trusted)")
    }
    if trusted, popover.isShown, captureHold.hasStarted {
      if captureHold.shouldDismiss(
        overlayVisible: screenshotOverlayVisible(), at: ProcessInfo.processInfo.systemUptime)
      {
        finishCapture()
      }
      return
    }
    if Date() < resultUntil { return }
    let mouse = NSEvent.mouseLocation
    if trusted, popover.isShown,
      popoverController.view.window?.frame.insetBy(dx: -5, dy: -5).contains(mouse) == true
    {
      lastSeen = Date()
      return
    }
    guard trusted, let app = NSWorkspace.shared.frontmostApplication,
      HoverHost.shouldInspect(
        processIdentifier: app.processIdentifier,
        ownProcessIdentifier: ProcessInfo.processInfo.processIdentifier)
    else {
      hide()
      lastPoint = nil
      lastPID = nil
      return
    }
    if let dismissed = captureDismissedAnchor {
      let top = NSScreen.screens.first?.frame.maxY ?? 0
      if dismissed.contains(CGPoint(x: mouse.x, y: top - mouse.y)) { return }
      captureDismissedAnchor = nil
    }
    if actionBusy || confirmation != nil { return }
    let optionHeld = NSEvent.modifierFlags.contains(.option)
    let permitted = !preferences.optionHover || optionHeld
    let primaryTop = NSScreen.screens.first?.frame.maxY ?? 0
    let axMouse = CGPoint(x: mouse.x, y: primaryTop - mouse.y)
    if !permitted || anchor?.contains(axMouse) != true {
      gate.reset()
      hoverTask?.cancel()
      hoverTask = nil
      if popover.isShown, Date().timeIntervalSince(lastSeen) > 0.65 { hide() }
    } else if let ticket = gate.start(
      at: ProcessInfo.processInfo.systemUptime, delay: preferences.delayMS / 1000)
    {
      beginLoad(ticket: ticket)
    }
    let top = NSScreen.screens.first?.frame.maxY ?? 0
    let point = CGPoint(x: mouse.x, y: top - mouse.y)
    guard !busy,
      point != lastPoint || app.processIdentifier != lastPID
        || Date().timeIntervalSince(lastInspection) > 0.8
    else { return }
    lastPoint = point
    lastPID = app.processIdentifier
    lastInspection = Date()
    busy = true
    let pid = app.processIdentifier
    if debug {
      logger.info(
        "Foreground=\(app.localizedName ?? "unknown", privacy: .public) bundle=\(app.bundleIdentifier ?? "unknown", privacy: .public) mouse AX=\(point.debugDescription, privacy: .public)"
      )
    }
    inspector.inspect(point: point, pid: pid, debug: debug) { [weak self] result in
      Task { @MainActor in
        guard let self else { return }
        self.busy = false
        guard AXIsProcessTrusted(),
          NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
          hypot(NSEvent.mouseLocation.x - mouse.x, NSEvent.mouseLocation.y - mouse.y) < 10
        else { return }
        if self.debug {
          for line in result.diagnostics { self.logger.info("\(line, privacy: .public)") }
        }
        guard let link = result.link, let frame = result.frame else {
          // A browser tooltip can obscure the AX target during an existing dwell.
          guard
            self.gate.shouldResetOnMissingTarget(
              pointerInsideAnchor: self.anchor?.contains(point) == true)
          else { return }
          self.gate.reset()
          self.hoverTask?.cancel()
          self.hoverTask = nil
          return
        }
        let enabled = !self.preferences.optionHover || NSEvent.modifierFlags.contains(.option)
        guard enabled else { return }
        if self.targetLink != link {
          self.hide()
          self.targetLink = link
          self.showingQR = link.pullRequest == nil
        }
        self.anchor = frame
        self.lastSeen = Date()
        _ = self.gate.observe(
          link.url, at: ProcessInfo.processInfo.systemUptime, enabled: enabled)
        if let ticket = self.gate.start(
          at: ProcessInfo.processInfo.systemUptime, delay: self.preferences.delayMS / 1000)
        {
          self.beginLoad(ticket: ticket)
        }
      }
    }
  }

  private func show(link: HoverLink, axFrame: CGRect, top: CGFloat, mouse: CGPoint) {
    let frame = CGRect(
      x: axFrame.minX, y: top - axFrame.maxY, width: axFrame.width, height: axFrame.height)
    guard let screen = NSScreen.screens.first(where: { $0.frame.contains(mouse) }) else { return }
    let visible = screen.visibleFrame.insetBy(dx: 6, dy: 6)
    let size =
      CGSize(
        width: 360,
        height: showingQR
          ? 354
          : preferences.skipConfirmation && displayState.canApprove && confirmation == nil
            ? 280 : 260)
    guard
      let proposed = PanelPlacement.frame(
        link: frame, cursor: CGPoint(x: mouse.x - size.width / 2, y: mouse.y), visible: visible,
        size: size)
    else { return }
    var action: Selector?
    if confirmation != nil {
      action = #selector(confirmApproval)
    } else if displayState.canApprove {
      action = #selector(approveClicked)
    } else if displayState == .configure {
      action = #selector(openSettings)
    } else if displayState == .error {
      action = #selector(retryLoad)
    }
    let content = HoverCardView(
      size: size, link: link.pullRequest, url: link.url, qrMode: showingQR, qrImage: qrImage,
      qrStatus: qrStatus, copyAction: #selector(copyQR), copyLinkAction: #selector(copyLink),
      snapshot: displayedSnapshot, state: displayState,
      error: errorMessage, confirmation: confirmation, busy: actionBusy, target: self,
      action: action, cancelAction: #selector(cancelConfirmation),
      skipConfirmation: preferences.skipConfirmation,
      theme: .mint)
    popoverController.view = content
    popover.contentSize = size
    anchorPanel.setFrame(frame, display: false)
    guard let anchorView = anchorPanel.contentView else { return }
    anchorPanel.orderFrontRegardless()
    // AppKit owns the arrow, material, shadow and screen-edge adjustment.
    popover.show(
      relativeTo: anchorView.bounds, of: anchorView,
      preferredEdge: proposed.maxY <= frame.minY ? .minY : .maxY)
  }

  func popoverShouldDetach(_ popover: NSPopover) -> Bool { false }

  func popoverDidClose(_ notification: Notification) {
    if !closingPopover, targetLink != nil { hide() }
  }

  private func render() {
    guard let targetLink, let anchor else { return }
    let top = NSScreen.screens.first?.frame.maxY ?? 0
    // Anchor to the link, including while the pointer is on the panel.
    let point = CGPoint(x: anchor.midX, y: top - anchor.midY)
    show(link: targetLink, axFrame: anchor, top: top, mouse: point)
  }
  private func beginLoad(ticket: UUID) {
    guard let targetLink else { return }
    if showingQR {
      qrImage = QRGenerator.makeQRCode(from: targetLink.url.absoluteString, size: 600)
      qrStatus = qrImage == nil ? "This link is too long to encode." : nil
      render()
    }
    guard let current else { return }
    let key = PRKey(current)
    let targetURL = targetLink.url
    let epoch = uiEpoch
    displayedSnapshot = nil
    errorMessage = nil
    displayState = .loading
    render()
    hoverTask?.cancel()
    hoverTask = Task {
      do {
        guard key.organization == preferences.organization.lowercased() else {
          throw preferences.organization.isEmpty ? APIError.missingPAT : APIError.wrongOrganization
        }
        let value = try await service.snapshotForHover(key)
        try Task.checkCancellation()
        guard epoch == uiEpoch, gate.accepts(ticket, key: targetURL) else { return }
        let resolved = await service.state(key, snapshot: value)
        guard epoch == uiEpoch, gate.accepts(ticket, key: targetURL), !Task.isCancelled else {
          return
        }
        displayedSnapshot = value
        displayState = resolved
        render()
      } catch is CancellationError {} catch {
        guard !Task.isCancelled, epoch == uiEpoch, gate.accepts(ticket, key: targetURL) else {
          return
        }
        errorMessage = safeMessage(error)
        displayState =
          (error as? APIError == .missingPAT || error as? APIError == .wrongOrganization)
          ? .configure : .error
        render()
      }
    }
  }
  @objc private func copyLink() {
    guard showingQR, let targetLink else { return }
    let pasteboard = NSPasteboard.general
    pasteboard.clearContents()
    if pasteboard.setString(targetLink.url.absoluteString, forType: .string) {
      captureDismissedAnchor = anchor
      hide()
    } else {
      qrStatus = "Could not copy link."
      render()
    }
  }
  @objc private func copyQR() {
    guard showingQR, let qrImage, let png = QRGenerator.pngData(for: qrImage) else { return }
    let pasteboard = NSPasteboard.general
    pasteboard.clearContents()
    if pasteboard.setData(png, forType: .png) {
      // Suppress reopening until the pointer leaves the copied link.
      captureDismissedAnchor = anchor
      hide()
    } else {
      qrStatus = "Could not copy image."
      render()
    }
  }
  @objc private func retryLoad() { beginLoad(ticket: gate.ticket) }
  @objc private func approveClicked() {
    guard !actionBusy, let current, let displayedSnapshot, displayedSnapshot.eligible,
      let head = displayedSnapshot.pr.head
    else { return }
    actionBusy = true
    displayState = .loading
    render()
    let epoch = uiEpoch
    let skipConfirmation = preferences.skipConfirmation
    actionTask = Task {
      do {
        if skipConfirmation {
          let refreshed = try await service.quickApprove(
            PRKey(current), displayedHead: head, enabledByUser: skipConfirmation)
          guard epoch == uiEpoch else { return }
          approvalSucceeded(refreshed)
          return
        }
        let prepared = try await service.prepare(PRKey(current), displayedHead: head)
        try Task.checkCancellation()
        guard epoch == uiEpoch else {
          await service.discard(prepared)
          return
        }
        confirmation = prepared
        self.displayedSnapshot = prepared.snapshot
        displayState = .approve
        errorMessage = nil
        actionBusy = false
        render()
      } catch {
        guard epoch == uiEpoch else { return }
        actionBusy = false
        displayState = .error
        errorMessage = safeMessage(error)
        render()
      }
    }
  }
  @objc private func cancelConfirmation() {
    if let confirmation { Task { await service.discard(confirmation) } }
    confirmation = nil
    if let displayedSnapshot { displayState = .resolve(displayedSnapshot, reviewedHead: nil) }
    render()
  }
  @objc private func confirmApproval() {
    guard !actionBusy, let confirmation else { return }
    let epoch = uiEpoch
    self.confirmation = nil
    actionBusy = true
    displayState = .loading
    render()
    actionTask = Task {
      do {
        let refreshed = try await service.approve(confirmation, explicitlyConfirmed: true)
        guard epoch == uiEpoch else { return }
        approvalSucceeded(refreshed)
      } catch {
        guard epoch == uiEpoch else { return }
        actionBusy = false
        displayedSnapshot = nil
        displayState = .error
        errorMessage = safeMessage(error)
        render()
      }
    }
  }
  private func approvalSucceeded(_ refreshed: PRSnapshot) {
    displayedSnapshot = refreshed
    actionBusy = false
    displayState = .approved
    render()
    resultUntil = Date().addingTimeInterval(3)
    if preferences.successNotification { notifySuccess() }
  }

  private func notifySuccess() {
    let center = UNUserNotificationCenter.current()
    center.requestAuthorization(options: [.alert]) { granted, _ in
      guard granted else { return }
      let content = UNMutableNotificationContent()
      content.title = "LinkPeek"
      content.body = "Your approval was confirmed by Azure DevOps."
      UNUserNotificationCenter.current().add(
        UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }
  }

  func applicationWillTerminate(_ notification: Notification) {
    if let screenshotMonitor { NSEvent.removeMonitor(screenshotMonitor) }
  }
}
