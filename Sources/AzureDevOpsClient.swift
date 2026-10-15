import Foundation
import OSLog

struct PRKey: Hashable, Sendable {
  let organization: String
  let project: String
  let repository: String
  let id: String
  init(_ link: PRLink) {
    organization = link.organization.lowercased()
    project = link.project
    repository = link.repository
    id = link.id
  }
}

struct PullRequest: Decodable, Sendable, Equatable {
  struct Author: Decodable, Sendable, Equatable { let displayName: String }
  var createdBy: Author? = nil
  var sourceRefName: String? = nil
  var targetRefName: String? = nil
  struct Commit: Decodable, Sendable, Equatable { let commitId: String }
  struct Repository: Decodable, Sendable, Equatable {
    let id: String
    let name: String
  }
  let pullRequestId: Int
  let title: String
  let status: String
  let isDraft: Bool?
  let lastMergeSourceCommit: Commit?
  let repository: Repository
  var head: String? { lastMergeSourceCommit?.commitId }
}
struct Reviewer: Decodable, Sendable, Equatable {
  let id: String
  let vote: Int
  let isContainer: Bool?
  var approved: Bool { vote == 10 || vote == 5 }
}
struct ConnectionData: Decodable, Sendable {
  struct Identity: Decodable, Sendable {
    let id: String
    let isContainer: Bool?
  }
  let authenticatedUser: Identity
  var reviewerID: String? {
    guard UUID(uuidString: authenticatedUser.id) != nil, authenticatedUser.isContainer != true
    else { return nil }
    return authenticatedUser.id.lowercased()
  }
}
struct PRSnapshot: Sendable, Equatable {
  let pr: PullRequest
  let reviewerID: String
  let vote: Int
  var approved: Bool { vote == 10 || vote == 5 }
  var eligible: Bool {
    pr.status == "active" && pr.isDraft != true && pr.head != nil && [0, -5, -10].contains(vote)
  }
  var statusCaption: String {
    let lifecycle =
      pr.status == "active"
      ? (pr.isDraft == true ? "Draft" : "Active")
      : pr.status == "completed"
        ? "Completed" : pr.status == "abandoned" ? "Abandoned" : "Unknown status"
    let feedback: String
    switch vote {
    case 10: feedback = "Approved"
    case 5: feedback = "Approved with suggestions"
    case 0: feedback = "No vote"
    case -5: feedback = "Waiting for author"
    case -10: feedback = "Rejected"
    default: feedback = "Unknown vote"
    }
    return "\(lifecycle) · Your vote: \(feedback)"
  }

}

enum APIError: Error, LocalizedError, Equatable {
  case httpStatus(Int)
  case missingPAT, wrongOrganization, unauthorized, forbidden, notFound, rateLimited, timeout
  case network, malformed, server, identity, inactive, draft, alreadyApproved, shaChanged,
    unconfirmed, busy, unverified
  var errorDescription: String? {
    switch self {
    case .httpStatus(let code): "Azure DevOps returned HTTP \(code)"
    case .missingPAT: "Configure PAT"
    case .wrongOrganization: "Configure this organization"
    case .unauthorized: "PAT expired, revoked or invalid"
    case .forbidden: "PAT or account lacks permission"
    case .notFound: "PR unavailable or not found"
    case .rateLimited: "Rate limited. Retry later"
    case .timeout: "Request timed out"
    case .network: "Network unavailable"
    case .malformed: "Unexpected API response"
    case .server: "Azure DevOps request failed"
    case .identity: "Cannot verify authenticated reviewer"
    case .inactive: "PR is completed or abandoned"
    case .draft: "Draft PR cannot be approved"
    case .alreadyApproved: "Already approved"
    case .shaChanged: "New commits. Review changes before approving"
    case .unconfirmed: "Confirmation expired. Start again"
    case .busy: "Approval already in progress"
    case .unverified: "Approval outcome unknown. Refresh before retrying"
    }
  }
}

protocol HTTPTransport: Sendable {
  func data(for request: URLRequest) async throws -> (Data, HTTPURLResponse)
}
// Never forward Basic credentials through redirects, and disable HTTP/cookie caches.
private final class RedirectBlocker: NSObject, URLSessionTaskDelegate, Sendable {
  func urlSession(
    _ session: URLSession, task: URLSessionTask,
    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
    completionHandler: @escaping @Sendable (URLRequest?) -> Void
  ) { completionHandler(nil) }
}
final class SecureTransport: HTTPTransport, Sendable {
  private let session: URLSession
  init() {
    let config = URLSessionConfiguration.ephemeral
    config.urlCache = nil
    config.httpCookieStorage = nil
    config.httpShouldSetCookies = false
    config.timeoutIntervalForRequest = 15
    config.timeoutIntervalForResource = 30
    session = URLSession(configuration: config, delegate: RedirectBlocker(), delegateQueue: nil)
  }
  func data(for request: URLRequest) async throws -> (Data, HTTPURLResponse) {
    let (data, response) = try await session.data(for: request)
    guard let http = response as? HTTPURLResponse else { throw APIError.malformed }
    return (data, http)
  }
}

actor AzureDevOpsClient {
  private let credentials: any CredentialStore
  private let transport: any HTTPTransport
  init(
    credentials: any CredentialStore = KeychainCredentialStore(),
    transport: any HTTPTransport = SecureTransport()
  ) {
    self.credentials = credentials
    self.transport = transport
  }
  private func readToken(organization: String) throws -> String {
    do {
      guard let stored = try credentials.read(organization: organization), !stored.isEmpty else {
        throw APIError.missingPAT
      }
      return stored
    } catch let error as APIError { throw error } catch { throw CredentialError.unavailable }
  }
  static func validOrganization(_ name: String) -> Bool {
    !name.isEmpty && name.count <= 100
      && name.allSatisfy { $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "-") }
  }
  private func request<T: Decodable & Sendable>(
    _ type: T.Type, key: PRKey, suffix: [String]?, method: String = "GET", body: Data? = nil,
    suppliedToken: String? = nil
  ) async throws -> T {
    guard Self.validOrganization(key.organization) else { throw APIError.wrongOrganization }
    let token = try suppliedToken ?? readToken(organization: key.organization)
    let segments =
      suffix.map {
        [
          key.organization, key.project, "_apis", "git", "repositories", key.repository,
          "pullRequests", key.id,
        ] + $0
      } ?? [key.organization, "_apis", "connectionData"]
    guard segments.allSatisfy({ !$0.isEmpty && $0 != "." && $0 != ".." }) else {
      throw APIError.malformed
    }
    let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-._~"))
    var components = URLComponents()
    components.scheme = "https"
    components.host = "dev.azure.com"
    components.percentEncodedPath =
      "/"
      + segments.map { $0.addingPercentEncoding(withAllowedCharacters: allowed)! }.joined(
        separator: "/")
    // connectionData is an unversioned bootstrap endpoint, as used by Microsoft's SDK.
    // Git details/reviewers/votes retain REST version 7.1.
    if suffix != nil { components.queryItems = [URLQueryItem(name: "api-version", value: "7.1")] }
    guard let url = components.url, url.scheme == "https", url.host == "dev.azure.com" else {
      throw APIError.malformed
    }
    var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData)
    request.httpMethod = method
    request.httpBody = body
    request.setValue(
      "Basic " + Data((":" + token).utf8).base64EncodedString(), forHTTPHeaderField: "Authorization"
    )
    request.setValue("application/json", forHTTPHeaderField: "Accept")
    if body != nil { request.setValue("application/json", forHTTPHeaderField: "Content-Type") }
    let data: Data
    let response: HTTPURLResponse
    do { (data, response) = try await transport.data(for: request) } catch is CancellationError {
      throw CancellationError()
    } catch let error as URLError {
      if error.code == .cancelled { throw CancellationError() }
      throw error.code == .timedOut ? APIError.timeout : APIError.network
    } catch let error as APIError { throw error } catch { throw APIError.network }
    Logger(subsystem: "com.linkpeek.app", category: "API").notice(
      "API endpoint=\(suffix == nil ? "connectionData" : "git", privacy: .public) status=\(response.statusCode)"
    )
    switch response.statusCode {
    case 200...299: break
    case 401: throw APIError.unauthorized
    case 403: throw APIError.forbidden
    case 404: throw APIError.notFound
    case 429: throw APIError.rateLimited
    case 500...599: throw APIError.server
    default: throw APIError.httpStatus(response.statusCode)
    }
    do { return try JSONDecoder().decode(T.self, from: data) } catch { throw APIError.malformed }
  }
  func identity(organization: String, token: String? = nil) async throws -> String {
    let link = PRLink("https://dev.azure.com/\(organization)/p/_git/r/pullrequest/1")
    guard let link else { throw APIError.wrongOrganization }
    let data = try await request(
      ConnectionData.self, key: PRKey(link), suffix: nil, suppliedToken: token)
    guard let id = data.reviewerID else { throw APIError.identity }
    return id
  }
  func snapshot(_ key: PRKey, token supplied: String? = nil) async throws -> PRSnapshot {
    let token = try supplied ?? readToken(organization: key.organization)
    let identity = try await identity(organization: key.organization, token: token)
    let pr = try await request(PullRequest.self, key: key, suffix: [], suppliedToken: token)
    guard String(pr.pullRequestId) == key.id, UUID(uuidString: pr.repository.id) != nil else {
      throw APIError.malformed
    }
    if pr.status == "active" && pr.isDraft != true {
      guard let head = pr.head, head.count == 40, head.allSatisfy({ $0.isHexDigit && $0.isASCII })
      else { throw APIError.malformed }
    }
    struct List: Decodable, Sendable { let value: [Reviewer] }
    let reviewers = try await request(
      List.self, key: key, suffix: ["reviewers"], suppliedToken: token)
    let reviewer = reviewers.value.first {
      $0.id.lowercased() == identity && $0.isContainer != true
    }
    return PRSnapshot(pr: pr, reviewerID: identity, vote: reviewer?.vote ?? 0)
  }
  func vote(_ key: PRKey, reviewerID: String, expectedHead: String) async throws -> Reviewer {
    let token = try readToken(organization: key.organization)
    let latest = try await snapshot(key, token: token)
    guard latest.reviewerID == reviewerID.lowercased() else { throw APIError.identity }
    guard latest.pr.status == "active" else { throw APIError.inactive }
    guard latest.pr.isDraft != true else { throw APIError.draft }
    guard latest.pr.head == expectedHead else { throw APIError.shaChanged }
    guard !latest.approved else { throw APIError.alreadyApproved }
    try Task.checkCancellation()
    guard UUID(uuidString: reviewerID) != nil else { throw APIError.identity }
    let body = try JSONSerialization.data(withJSONObject: ["id": reviewerID, "vote": 10])
    return try await request(
      Reviewer.self, key: key, suffix: ["reviewers", reviewerID], method: "PUT", body: body,
      suppliedToken: token)
  }
}
