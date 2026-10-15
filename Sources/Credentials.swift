import Foundation
import Security

protocol CredentialStore: Sendable {
  func read(organization: String) throws -> String?
  func save(_ token: String, organization: String) throws
  func remove(organization: String) throws
  func contains(organization: String) throws -> Bool
}

extension CredentialStore {
  func contains(organization: String) throws -> Bool { try read(organization: organization) != nil }
}

enum CredentialError: Error, LocalizedError {
  case unavailable
  var errorDescription: String? {
    "Keychain access failed. Check Keychain permissions and try again."
  }
}

struct KeychainCredentialStore: CredentialStore {
  let service: String
  // Preserve the original Keychain namespace when renaming the application.
  init(service: String = "com.prhover.app.azure-pat") { self.service = service }
  private func query(_ organization: String) -> [String: Any] {
    [
      kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
      kSecAttrAccount as String: organization.lowercased(), kSecAttrSynchronizable as String: false,
    ]
  }
  func contains(organization: String) throws -> Bool {
    var query = query(organization)
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    let status = SecItemCopyMatching(query as CFDictionary, nil)
    if status == errSecItemNotFound { return false }
    guard status == errSecSuccess else { throw CredentialError.unavailable }
    return true
  }
  func read(organization: String) throws -> String? {
    var query = query(organization)
    query[kSecReturnData as String] = true
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    var result: CFTypeRef?
    let status = SecItemCopyMatching(query as CFDictionary, &result)
    if status == errSecItemNotFound { return nil }
    guard status == errSecSuccess, let data = result as? Data,
      let token = String(data: data, encoding: .utf8)
    else { throw CredentialError.unavailable }
    return token
  }
  func save(_ token: String, organization: String) throws {
    guard !token.isEmpty else { throw CredentialError.unavailable }
    let data = Data(token.utf8)
    let status = SecItemUpdate(
      query(organization) as CFDictionary, [kSecValueData as String: data] as CFDictionary)
    if status == errSecItemNotFound {
      var item = query(organization)
      item[kSecValueData as String] = data
      item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
      guard SecItemAdd(item as CFDictionary, nil) == errSecSuccess else {
        throw CredentialError.unavailable
      }
    } else if status != errSecSuccess {
      throw CredentialError.unavailable
    }
  }
  func remove(organization: String) throws {
    let status = SecItemDelete(query(organization) as CFDictionary)
    guard status == errSecSuccess || status == errSecItemNotFound else {
      throw CredentialError.unavailable
    }
  }
}
