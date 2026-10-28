import AppKit
import ApplicationServices
import ServiceManagement
import SwiftUI

@MainActor
final class Preferences: ObservableObject {
  static let shared = Preferences()
  @Published var organization: String
  @Published var optionHover: Bool { didSet { persist() } }
  @Published var delayMS: Double { didSet { persist() } }
  @Published var skipConfirmation: Bool { didSet { persist() } }
  @Published var successNotification: Bool { didSet { persist() } }
  @Published var qrEnabled: Bool { didSet { persist() } }
  @Published var grabEnabled: Bool { didSet { persist() } }
  @Published var anyOtherLinkQR: Bool { didSet { persist() } }
  @Published var customPatterns: [String] { didSet { persist() } }
  @Published var prCardEnabled: Bool { didSet { persist() } }
  var changed: (() -> Void)?
  private let defaults: UserDefaults
  init(defaults: UserDefaults = .standard) {
    self.defaults = defaults
    if defaults === UserDefaults.standard {
      let legacy = defaults.persistentDomain(forName: "com.prhover.app") ?? [:]
      let current = defaults.persistentDomain(forName: "com.linkpeek.app") ?? [:]
      for (key, value) in AppInfo.legacyPreferences(legacy, current: current) {
        defaults.set(value, forKey: key)
      }
    }
    organization = defaults.string(forKey: "organization") ?? ""
    optionHover = defaults.bool(forKey: "optionHover")
    delayMS = HoverDelay.resolve(
      stored: defaults.double(forKey: "delayMS"),
      version: defaults.integer(forKey: "hoverPrefsVersion"))
    skipConfirmation = defaults.bool(forKey: "skipConfirmation")
    successNotification = defaults.bool(forKey: "successNotification")
    qrEnabled = defaults.object(forKey: "qrEnabled") as? Bool ?? true
    grabEnabled = defaults.object(forKey: "grabEnabled") as? Bool ?? true
    anyOtherLinkQR = defaults.bool(forKey: "anyOtherLinkQR")
    customPatterns = defaults.stringArray(forKey: "customPatterns") ?? []
    prCardEnabled = defaults.object(forKey: "prCardEnabled") as? Bool ?? true
    defaults.set(HoverDelay.currentVersion, forKey: "hoverPrefsVersion")
    defaults.set(delayMS, forKey: "delayMS")
  }
  var linkFilter: LinkFilter {
    LinkFilter(
      qrEnabled: qrEnabled, grabEnabled: grabEnabled, patterns: customPatterns,
      anyOtherLink: anyOtherLinkQR)
  }
  func saveOrganization(_ value: String) {
    organization = value.lowercased()
    persist()
  }
  private func persist() {
    defaults.set(organization, forKey: "organization")
    defaults.set(optionHover, forKey: "optionHover")
    defaults.set(delayMS, forKey: "delayMS")
    defaults.set(skipConfirmation, forKey: "skipConfirmation")
    defaults.set(successNotification, forKey: "successNotification")
    defaults.set(qrEnabled, forKey: "qrEnabled")
    defaults.set(grabEnabled, forKey: "grabEnabled")
    defaults.set(anyOtherLinkQR, forKey: "anyOtherLinkQR")
    defaults.set(customPatterns, forKey: "customPatterns")
    defaults.set(prCardEnabled, forKey: "prCardEnabled")
    changed?()
  }
}

@MainActor
struct PreferencesView: View {
  static let windowSize = NSSize(width: 720, height: 620)
  @ObservedObject var preferences: Preferences
  let client: AzureDevOpsClient
  var grantAccessibility: () -> Void
  @State private var accessibilityGranted = AXIsProcessTrusted()
  @State private var loginStatus = SMAppService.mainApp.status
  @State private var loginMessage = ""
  @State private var organization = ""
  @State private var token = ""
  @State private var message = ""
  @State private var busy = false
  @State private var hasSavedToken = false
  @State private var showingLicense = false
  @State private var selection: SettingsPage = .general
  @State private var newPattern = ""
  @State private var patternError = ""
  @State private var testLink = ""
  private let accent = Color(nsColor: .systemBlue)

  var body: some View {
    HStack(spacing: 0) {
      VStack(alignment: .leading, spacing: 4) {
        HStack(spacing: 10) {
          Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 40, height: 40)
          VStack(alignment: .leading, spacing: 2) {
            Text("LinkPeek").font(.system(size: 15, weight: .semibold))
            Text("Settings").font(.caption).foregroundStyle(.secondary)
          }
        }.padding(.bottom, 22).padding(.top, 8)
        sidebarButton(.general)
        sidebarButton(.qrLinks)
        sidebarButton(.azureDevOps)
        Spacer()
        Divider().padding(.vertical, 8)
        sidebarButton(.about)
      }
      .padding(14).frame(width: 190)
      .background(Color(nsColor: .windowBackgroundColor))
      Divider()
      ScrollView {
        VStack(alignment: .leading, spacing: 18) {
          VStack(alignment: .leading, spacing: 5) {
            Text(selection.title).font(.system(size: 24, weight: .bold))
            Text(selection.subtitle).font(.system(size: 12)).foregroundStyle(.secondary)
          }.padding(.bottom, 4)
          switch selection {
          case .general: generalPage
          case .qrLinks: qrLinksPage
          case .azureDevOps: azurePage
          case .about: aboutPage
          }
        }.padding(24).frame(maxWidth: .infinity, alignment: .leading)
      }
      .frame(maxWidth: .infinity, maxHeight: .infinity)
      .background(Color(nsColor: .controlColor).opacity(0.35))
    }
    .tint(accent)
    .toggleStyle(.switch)
    .frame(width: Self.windowSize.width, height: Self.windowSize.height)
    .onReceive(Timer.publish(every: 1, on: .main, in: .common).autoconnect()) { _ in
      accessibilityGranted = AXIsProcessTrusted()
      loginStatus = SMAppService.mainApp.status
    }
    .onAppear {
      loginStatus = SMAppService.mainApp.status
      organization = preferences.organization
      refreshCredentialIndicator()
    }
    .onChange(of: organization) { _ in refreshCredentialIndicator() }
    .onDisappear { token = "" }
    .sheet(isPresented: $showingLicense) { licenseSheet }
  }

  private func sidebarButton(_ page: SettingsPage) -> some View {
    Button {
      selection = page
    } label: {
      HStack(spacing: 10) {
        Image(systemName: page.symbol).font(.system(size: 13, weight: .medium))
          .foregroundStyle(.white).frame(width: 26, height: 26)
          .background(accent.gradient, in: RoundedRectangle(cornerRadius: 6))
        Text(page.title).font(.system(size: 13))
          .foregroundStyle(selection == page ? accent : Color.primary)
        Spacer()
      }
      .padding(8).contentShape(Rectangle())
      .background(
        selection == page ? accent.opacity(0.12) : Color.clear,
        in: RoundedRectangle(cornerRadius: 8))
    }.buttonStyle(.plain)
  }

  private var generalPage: some View {
    VStack(alignment: .leading, spacing: 18) {
      SettingsCard {
        settingsRow("Launch at login", detail: "Ready when you sign in.") {
          Toggle(
            "Launch at login",
            isOn: Binding(
              get: { loginStatus == .enabled || loginStatus == .requiresApproval },
              set: { setLaunchAtLogin($0) })
          ).labelsHidden().tint(Color(nsColor: .systemGreen))
        }
        Divider()
        settingsRow(
          "Accessibility",
          detail: accessibilityGranted ? "Access granted" : "Access required to read hovered links."
        ) {
          Button(accessibilityGranted ? "Manage…" : "Grant access…", action: grantAccessibility)
        }
      }
      if loginStatus == .requiresApproval {
        HStack {
          Text("Allow LinkPeek in Login Items to finish enabling.").font(.caption)
          Button("Open Login Items…") { SMAppService.openSystemSettingsLoginItems() }
        }
      }
      if !loginMessage.isEmpty { Text(loginMessage).font(.caption).foregroundStyle(.red) }
      groupTitle("Hover timing")
      SettingsCard {
        settingsRow("Trigger", detail: "Applies to QR links and PR cards.") {
          Picker("Trigger", selection: $preferences.optionHover) {
            Text("Delayed").tag(false)
            Text("Option + Hover").tag(true)
          }.labelsHidden().frame(width: 135)
        }
        Divider()
        VStack(alignment: .leading, spacing: 10) {
          HStack {
            Text("Hover delay")
            Spacer()
            Text("\(Int(preferences.delayMS)) ms").monospacedDigit().foregroundStyle(.secondary)
          }
          Slider(value: $preferences.delayMS, in: HoverDelay.range, step: 50)
          HStack {
            Text("0.5 s")
            Spacer()
            Text("Default 0.8 s")
            Spacer()
            Text("1.5 s")
          }.font(.caption).foregroundStyle(.secondary)
        }
        .padding(16).disabled(preferences.optionHover).opacity(preferences.optionHover ? 0.45 : 1)
      }
      Text(
        "Cards follow your Mac’s light or dark appearance. QR codes are generated offline and close after copying. Apps must expose link targets through Accessibility."
      )
      .font(.caption).foregroundStyle(.secondary)
    }
  }

  private var qrLinksPage: some View {
    VStack(alignment: .leading, spacing: 18) {
      SettingsCard {
        settingsRow("Show QR on hover", detail: "Turn off to stop every QR card.") {
          Toggle("Show QR on hover", isOn: $preferences.qrEnabled).labelsHidden()
            .tint(Color(nsColor: .systemGreen))
        }
      }
      VStack(alignment: .leading, spacing: 18) {
        groupTitle("Built in")
        SettingsCard {
          settingsRow("Grab links", detail: LinkFilter.grabPattern) {
            Toggle("Grab links", isOn: $preferences.grabEnabled).labelsHidden()
              .tint(Color(nsColor: .systemGreen))
          }
        }
        groupTitle("Your patterns")
        SettingsCard {
          ForEach(Array(preferences.customPatterns.enumerated()), id: \.offset) { index, pattern in
            HStack {
              Image(systemName: "link").foregroundStyle(.secondary)
              Text(pattern).font(.system(size: 12, design: .monospaced))
              Spacer()
              Button {
                preferences.customPatterns.remove(at: index)
              } label: {
                Image(systemName: "xmark")
              }.buttonStyle(.plain).accessibilityLabel("Remove pattern")
            }.padding(.horizontal, 16).padding(.vertical, 10)
            Divider()
          }
          HStack {
            TextField("github.com/your-org/*", text: $newPattern)
              .textFieldStyle(.roundedBorder).onSubmit(addPattern)
              .onChange(of: newPattern) { _ in patternError = "" }
            Button("Add", action: addPattern)
          }.padding(12)
        }
        if !patternError.isEmpty {
          Text(patternError).font(.caption).foregroundStyle(.red)
        }
        Text("Use * as a wildcard. Scheme is ignored, and matching isn’t case sensitive.")
          .font(.caption).foregroundStyle(.secondary)
        groupTitle("Everything else")
        SettingsCard {
          settingsRow(
            "Show QR for any other link", detail: "Off by default. Other links are ignored."
          ) {
            Toggle("Show QR for any other link", isOn: $preferences.anyOtherLinkQR)
              .labelsHidden().tint(Color(nsColor: .systemGreen))
          }
        }
        SettingsCard {
          HStack {
            Image(systemName: "flask").foregroundStyle(.secondary)
            TextField("Paste a link to test", text: $testLink).textFieldStyle(.roundedBorder)
            Text(testResult).font(.caption).foregroundStyle(.secondary)
              .frame(minWidth: 70, alignment: .trailing)
          }.padding(12)
        }
      }.disabled(!preferences.qrEnabled).opacity(preferences.qrEnabled ? 1 : 0.45)
    }
  }

  private var testResult: String {
    let text = testLink.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !text.isEmpty else { return "" }
    let value = text.contains("://") ? text : "https://" + text
    guard let url = URL(string: value) else { return "Ignored" }
    return preferences.linkFilter.allowsQR(url) ? "Shows QR" : "Ignored"
  }

  private func addPattern() {
    guard let pattern = LinkPattern.normalize(newPattern) else {
      patternError = "Enter a pattern like github.com/your-org/*"
      return
    }
    guard pattern != LinkFilter.grabPattern, !preferences.customPatterns.contains(pattern) else {
      patternError = "That pattern is already covered"
      return
    }
    preferences.customPatterns.append(pattern)
    newPattern = ""
  }

  private var azurePage: some View {
    VStack(alignment: .leading, spacing: 18) {
      SettingsCard {
        VStack(alignment: .leading, spacing: 12) {
          Text("Connection").font(.system(size: 14, weight: .semibold))
          TextField("Organization name", text: $organization).textFieldStyle(.roundedBorder)
          Text("Personal access token").font(.caption).foregroundStyle(.secondary)
          PATEntryField(text: $token, hasSavedToken: hasSavedToken)
            .frame(height: 28)
          if hasSavedToken {
            Label("PAT saved in Keychain", systemImage: "lock.fill").font(.caption).foregroundStyle(
              .secondary)
          }
          HStack {
            Button("Save PAT") { save() }.buttonStyle(.borderedProminent).disabled(
              busy || token.isEmpty)
            Button("Remove") { remove() }.disabled(busy || !hasSavedToken)
            Spacer()
            if busy { ProgressView().controlSize(.small) }
            Button("Test Connection") { test() }.disabled(busy || preferences.organization.isEmpty)
          }
          if !message.isEmpty { Text(message).font(.caption).textSelection(.enabled) }
        }.padding(16)
      }
      Text(
        "Use the organization name, not its URL. Code (Read & Write) scope is required for approval. Tokens stay in this Mac’s Keychain and are never displayed."
      )
      .font(.caption).foregroundStyle(.secondary)
      groupTitle("Pull requests")
      SettingsCard {
        settingsRow("Show PR card on hover", detail: "PR links show status instead of a QR card.") {
          Toggle("Show PR card on hover", isOn: $preferences.prCardEnabled).labelsHidden()
            .tint(Color(nsColor: .systemGreen))
        }
      }
      groupTitle("Approval")
      SettingsCard {
        settingsRow(
          "Skip confirmation", detail: "An Approve click submits immediately after verification."
        ) {
          Toggle("Skip confirmation", isOn: $preferences.skipConfirmation).labelsHidden()
            .tint(Color(nsColor: .systemGreen))
        }
        Divider()
        settingsRow("Success notification", detail: "Notify after Azure DevOps confirms your vote.")
        {
          Toggle("Success notification", isOn: $preferences.successNotification).labelsHidden()
            .tint(Color(nsColor: .systemGreen))
        }
      }
      Label("Hovering never casts a vote.", systemImage: "checkmark.shield").font(.caption)
        .foregroundStyle(.secondary)
    }
  }

  private var aboutPage: some View {
    VStack(alignment: .leading, spacing: 18) {
      SettingsCard {
        VStack(spacing: 14) {
          Image(nsImage: NSApp.applicationIconImage).resizable().interpolation(.high)
            .frame(width: 104, height: 104).shadow(color: .black.opacity(0.12), radius: 8, y: 4)
          Text("LinkPeek").font(.system(size: 25, weight: .bold))
          Text(AppInfo.versionLine(info: Bundle.main.infoDictionary))
            .font(.caption).foregroundStyle(.secondary)
            .padding(.horizontal, 12).padding(.vertical, 5)
            .background(Color.primary.opacity(0.06), in: Capsule())
          Text("A little more behind every link.").font(.system(size: 14, weight: .medium))
          Text(
            "Preview Azure DevOps pull requests and turn web links into shareable QR codes, right where you hover."
          )
          .font(.system(size: 12)).foregroundStyle(.secondary).multilineTextAlignment(.center)
          Button("MIT License") { showingLicense = true }.foregroundStyle(accent)
          HStack(spacing: 10) {
            Link(destination: AppInfo.repositoryURL) {
              Label("GitHub", systemImage: "chevron.left.forwardslash.chevron.right")
                .font(.system(size: 13, weight: .medium))
                .padding(.horizontal, 14).padding(.vertical, 7)
                .background(Capsule().fill(accent)).foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            .help(AppInfo.repositoryURL.absoluteString)
            Button {
              NSApp.terminate(nil)
            } label: {
              Label("Quit", systemImage: "power")
                .font(.system(size: 13, weight: .medium))
                .padding(.horizontal, 14).padding(.vertical, 7)
                .background(Capsule().fill(Color.primary.opacity(0.06)))
            }.buttonStyle(.plain)
          }.padding(.top, 4)
        }.padding(28).frame(maxWidth: .infinity)
      }
      Text(
        "Built with native macOS popovers, Accessibility and Core Image. QR generation stays on your Mac."
      )
      .font(.caption).foregroundStyle(.secondary)
    }
  }

  private var licenseSheet: some View {
    VStack(alignment: .leading, spacing: 16) {
      Text("MIT License").font(.title2.bold())
      ScrollView {
        Text(licenseText).font(.system(size: 12)).textSelection(.enabled)
          .frame(maxWidth: .infinity, alignment: .leading)
      }
      HStack {
        Spacer()
        Button("Done") { showingLicense = false }.keyboardShortcut(.defaultAction)
      }
    }.padding(24).frame(width: 540, height: 460)
  }

  private var licenseText: String {
    guard let url = Bundle.main.url(forResource: "LICENSE", withExtension: nil),
      let text = try? String(contentsOf: url, encoding: .utf8)
    else { return "The license file could not be loaded." }
    return text
  }

  private func groupTitle(_ title: String) -> some View {
    Text(title).font(.system(size: 12, weight: .semibold)).foregroundStyle(.secondary)
      .padding(.leading, 4)
  }

  private func settingsRow<Control: View>(
    _ title: String, detail: String, @ViewBuilder control: () -> Control
  ) -> some View {
    HStack(spacing: 16) {
      VStack(alignment: .leading, spacing: 4) {
        Text(title).font(.system(size: 13))
        Text(detail).font(.system(size: 11)).foregroundStyle(.secondary)
      }
      Spacer(minLength: 10)
      control()
    }.padding(16)
  }

  private func setLaunchAtLogin(_ enabled: Bool) {
    loginMessage = ""
    do {
      if enabled {
        try SMAppService.mainApp.register()
      } else {
        try SMAppService.mainApp.unregister()
      }
    } catch {
      loginMessage = "Could not update launch at login. Check Login Items in System Settings."
    }
    loginStatus = SMAppService.mainApp.status
  }

  private func refreshCredentialIndicator() {
    let org = organization.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    guard AzureDevOpsClient.validOrganization(org) else {
      hasSavedToken = false
      return
    }
    Task {
      let exists =
        (try? await Task.detached { try KeychainCredentialStore().contains(organization: org) }
          .value) ?? false
      guard organization.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == org else {
        return
      }
      hasSavedToken = exists
    }
  }
  private func save() {
    let org = organization.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    guard AzureDevOpsClient.validOrganization(org) else {
      message = "Enter an organization name, not a URL."
      return
    }
    busy = true
    let secret = token
    token = ""
    Task {
      do {
        try await Task.detached {
          let store = KeychainCredentialStore()
          try store.save(secret, organization: org)
          guard try store.read(organization: org) == secret else {
            throw CredentialError.unavailable
          }
        }.value
        preferences.saveOrganization(org)
        hasSavedToken = true
        message = "PAT saved to Keychain."
      } catch { message = "Could not save PAT to Keychain." }
      busy = false
    }
  }
  private func remove() {
    let org = organization.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    guard AzureDevOpsClient.validOrganization(org) else {
      message = "Enter an organization name."
      return
    }
    token = ""
    busy = true
    Task {
      do {
        try await Task.detached { try KeychainCredentialStore().remove(organization: org) }.value
        // Removing the configured credential also clears its active organization.
        if preferences.organization == org {
          preferences.saveOrganization("")
          organization = ""
        } else {
          preferences.changed?()
        }
        hasSavedToken = false
        message = "PAT removed from Keychain."
      } catch { message = "Could not remove PAT from Keychain." }
      busy = false
    }
  }
  private func test() {
    busy = true
    Task {
      do {
        _ = try await client.identity(organization: preferences.organization)
        message = "Connected. Authenticated reviewer identity verified."
      } catch { message = safeMessage(error) }
      busy = false
    }
  }
}

func safeMessage(_ error: Error) -> String {
  if let error = error as? APIError { return error.errorDescription ?? "Request failed" }
  if error is CredentialError { return "Keychain access failed" }
  return "Request failed"
}

private enum SettingsPage: String {
  case general, qrLinks, azureDevOps, about
  var title: String {
    switch self {
    case .general: "General"
    case .qrLinks: "QR links"
    case .azureDevOps: "Azure DevOps"
    case .about: "About"
    }
  }
  var symbol: String {
    switch self {
    case .general: "gearshape.fill"
    case .qrLinks: "qrcode"
    case .azureDevOps: "checkmark.bubble.fill"
    case .about: "info.circle.fill"
    }
  }
  var subtitle: String {
    switch self {
    case .general: "Make LinkPeek feel at home on your Mac."
    case .qrLinks: "Hover a link to get its QR code. Only the links below show a card."
    case .azureDevOps: "Connect your account and choose how you approve."
    case .about: "Small utility. Useful links."
    }
  }
}

private struct SettingsCard<Content: View>: View {
  @ViewBuilder var content: Content
  var body: some View {
    VStack(spacing: 0) { content }
      .frame(maxWidth: .infinity)
      .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 14))
      .shadow(color: .black.opacity(0.04), radius: 4, y: 1)
  }
}

// Both saved-state and entered masks are rendered by NSSecureTextField cells.
// The fixed saved-state sample is visual only and never enters the token binding.
@MainActor
private struct PATEntryField: NSViewRepresentable {
  @Binding var text: String
  var hasSavedToken: Bool

  func makeCoordinator() -> Coordinator { Coordinator(text: $text) }

  func makeNSView(context: Context) -> PATSecureField {
    let field = PATSecureField()
    field.font = .systemFont(ofSize: 18, weight: .medium)
    field.isBezeled = true
    field.bezelStyle = .roundedBezel
    field.delegate = context.coordinator
    field.setAccessibilityLabel("Personal access token")
    return field
  }

  func updateNSView(_ field: PATSecureField, context: Context) {
    context.coordinator.text = $text
    if field.stringValue != text { field.stringValue = text }
    field.showsSavedMask = hasSavedToken && text.isEmpty
    field.needsDisplay = true
  }

  @MainActor
  final class Coordinator: NSObject, NSTextFieldDelegate {
    var text: Binding<String>
    init(text: Binding<String>) { self.text = text }
    func controlTextDidChange(_ notification: Notification) {
      guard let field = notification.object as? PATSecureField else { return }
      text.wrappedValue = field.stringValue
      field.showsSavedMask = field.stringValue.isEmpty && field.showsSavedMask
      field.needsDisplay = true
    }
  }
}
