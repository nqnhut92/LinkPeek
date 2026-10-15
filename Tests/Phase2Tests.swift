import Foundation
import Testing

@testable import LinkPeek

private let identityID = "11111111-1111-1111-1111-111111111111"
private let repositoryID = "22222222-2222-2222-2222-222222222222"
private let headA = String(repeating: "a", count: 40)
private let headB = String(repeating: "b", count: 40)
private let testLink = PRLink("https://dev.azure.com/org/Project%20Name/_git/repo/pullrequest/42")!

final class MemoryCredentials: CredentialStore, @unchecked Sendable {
  private let lock = NSLock()
  private var values: [String: String] = [:]
  func read(organization: String) throws -> String? { lock.withLock { values[organization] } }
  func save(_ token: String, organization: String) throws {
    lock.withLock { values[organization] = token }
  }
  func remove(organization: String) throws {
    _ = lock.withLock { values.removeValue(forKey: organization) }
  }
}
private final class TestClock: @unchecked Sendable {
  private let lock = NSLock()
  private var instant = Date(timeIntervalSince1970: 1000)
  func now() -> Date { lock.withLock { instant } }
  func advance(_ seconds: Double) { lock.withLock { instant.addTimeInterval(seconds) } }
}
private actor MockTransport: HTTPTransport {
  var heads: [String] = [headA]
  var status = "active"
  var draft = false
  var vote = 0
  var statusCode = 200
  var transportError: URLError?
  var identity = identityID
  var count = 0
  var puts = 0
  var failRefresh = false
  var pause = false
  var captured: [URLRequest] = []
  func configure(
    heads: [String]? = nil, status: String? = nil, draft: Bool? = nil, vote: Int? = nil,
    statusCode: Int? = nil, identity: String? = nil, transportError: URLError? = nil,
    pause: Bool? = nil, failRefresh: Bool? = nil
  ) {
    if let heads { self.heads = heads }
    if let status { self.status = status }
    if let draft { self.draft = draft }
    if let vote { self.vote = vote }
    if let statusCode { self.statusCode = statusCode }
    if let identity { self.identity = identity }
    self.transportError = transportError
    if let pause { self.pause = pause }
    if let failRefresh { self.failRefresh = failRefresh }
  }
  func data(for request: URLRequest) async throws -> (Data, HTTPURLResponse) {
    count += 1
    captured.append(request)
    if pause { try await Task.sleep(for: .milliseconds(30)) }
    if let transportError { throw transportError }
    let path = request.url!.path
    var json: [String: Any]
    var code = statusCode
    if request.httpMethod == "PUT" {
      puts += 1
      let body = try JSONSerialization.jsonObject(with: request.httpBody!) as! [String: Any]
      #expect(body["vote"] as? Int == 10)
      #expect(body["id"] as? String == identityID)
      #expect(path.hasSuffix("/reviewers/" + identityID))
      vote = 10
      json = ["id": identityID, "vote": 10]
    } else if path.hasSuffix("connectionData") {
      json = ["authenticatedUser": ["id": identity, "isContainer": false]]
    } else if path.hasSuffix("reviewers") {
      if failRefresh && puts > 0 { code = 503 }
      json = [
        "value": [
          ["id": identityID, "vote": vote, "isContainer": false],
          ["id": repositoryID, "vote": 10, "isContainer": true],
        ]
      ]
    } else {
      let head = heads.count > 1 ? heads.removeFirst() : heads[0]
      json = [
        "pullRequestId": 42, "title": "Test PR", "createdBy": ["displayName": "Sample Creator"],
        "sourceRefName": "refs/heads/feature", "targetRefName": "refs/heads/main", "status": status,
        "isDraft": draft,
        "lastMergeSourceCommit": ["commitId": head],
        "repository": ["id": repositoryID, "name": "repo"],
      ]
    }
    let data = try JSONSerialization.data(withJSONObject: json)
    return (
      data,
      HTTPURLResponse(
        url: request.url!, statusCode: code, httpVersion: "HTTP/1.1", headerFields: nil)!
    )
  }
}
private func setup(_ mock: MockTransport = MockTransport(), clock: TestClock = TestClock()) throws
  -> (AzureDevOpsClient, HoverService, MockTransport, TestClock)
{
  let credentials = MemoryCredentials()
  try credentials.save("synthetic-test-token", organization: "org")
  let client = AzureDevOpsClient(credentials: credentials, transport: mock)
  return (client, HoverService(client: client, now: { clock.now() }), mock, clock)
}

@Test func credentialAbstractionRoundTrip() throws {
  let store = MemoryCredentials()
  #expect(try store.read(organization: "org") == nil)
  #expect(try !store.contains(organization: "org"))
  try store.save("synthetic-one", organization: "org")
  try store.save("synthetic-two", organization: "org")
  #expect(try store.read(organization: "org") == "synthetic-two")
  #expect(try store.contains(organization: "org"))
  #expect(try store.read(organization: "other") == nil)
  try store.remove(organization: "org")
  #expect(try store.read(organization: "org") == nil)
}
@Test func parseResponseIdentityAndVote() async throws {
  let (client, _, mock, _) = try setup()
  let snapshot = try await client.snapshot(PRKey(testLink))
  #expect(snapshot.pr.title == "Test PR")
  #expect(snapshot.pr.createdBy?.displayName == "Sample Creator")
  #expect(snapshot.pr.sourceRefName == "refs/heads/feature")
  #expect(snapshot.reviewerID == identityID)
  #expect(snapshot.vote == 0)  // Group's vote must not count as mine.
  #expect(snapshot.pr.head == headA)
  let requests = await mock.captured
  #expect(requests.allSatisfy { $0.url?.scheme == "https" && $0.url?.host == "dev.azure.com" })
  #expect(requests.contains { $0.url!.absoluteString.contains("Project%20Name") })
  #expect(
    requests.filter { !$0.url!.path.hasSuffix("connectionData") }.allSatisfy {
      $0.url!.query == "api-version=7.1"
    })
  #expect(
    requests.filter { $0.url!.path.hasSuffix("connectionData") }.allSatisfy { $0.url!.query == nil }
  )
  await mock.configure(identity: "not-a-guid")
  await #expect(throws: APIError.identity) { try await client.identity(organization: "org") }
}
@Test(arguments: [10, 5, 0, -5, -10]) func voteInterpretation(_ vote: Int) {
  #expect(
    Reviewer(id: identityID, vote: vote, isContainer: false).approved == (vote == 10 || vote == 5))
}
@Test func hoverDelayAndStaleTickets() {
  var gate = HoverGate()
  let key = PRKey(testLink)
  let entered = gate.observe(key, at: 0, enabled: true)
  #expect(entered)
  #expect(gate.start(at: 0.499, delay: 0.5) == nil)
  let ticket = gate.start(at: 0.5, delay: 0.5)!
  #expect(gate.start(at: 1, delay: 0.5) == nil)
  #expect(gate.accepts(ticket, key: key))
  let left = gate.observe(nil, at: 1, enabled: true)
  #expect(!left)
  #expect(!gate.accepts(ticket, key: key))
  let blocked = gate.observe(key, at: 2, enabled: false)
  #expect(!blocked)
  #expect(gate.start(at: 3, delay: 0.5) == nil)
  _ = gate.observe(key, at: 4, enabled: true)
  #expect(gate.start(at: 4.2, delay: 0.3) == nil)
  #expect(gate.start(at: 4.301, delay: 0.3) != nil)
}
@Test func cacheExpiryAndDuplicatePrevention() async throws {
  let (_, service, mock, clock) = try setup()
  await mock.configure(pause: true)
  async let a = service.snapshot(PRKey(testLink))
  async let b = service.snapshot(PRKey(testLink))
  let values = try await (a, b)
  #expect(values.0 == values.1)
  #expect(await mock.count == 3)
  clock.advance(59)
  _ = try await service.snapshot(PRKey(testLink))
  #expect(await mock.count == 3)
  clock.advance(1)
  _ = try await service.snapshot(PRKey(testLink))
  #expect(await mock.count == 6)
}
@Test func invalidationRejectsStaleFetch() async throws {
  let (_, service, mock, _) = try setup()
  await mock.configure(pause: true)
  let task = Task { try await service.snapshot(PRKey(testLink)) }
  while await mock.count == 0 { await Task.yield() }
  await service.invalidate()
  await #expect(throws: CancellationError.self) { try await task.value }
}
@Test func confirmationAndSingleMutation() async throws {
  let (_, service, mock, _) = try setup()
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  await #expect(throws: APIError.unconfirmed) {
    try await service.approve(confirmation, explicitlyConfirmed: false)
  }
  #expect(await mock.puts == 0)
  let result = try await service.approve(confirmation, explicitlyConfirmed: true)
  #expect(result.vote == 10)
  #expect(await mock.puts == 1)
  await #expect(throws: APIError.unconfirmed) {
    try await service.approve(confirmation, explicitlyConfirmed: true)
  }
  #expect(await mock.puts == 1)
}
@Test func shaMismatchBeforeAndAfterConfirmation() async throws {
  let (_, service, mock, _) = try setup()
  await mock.configure(heads: [headB])
  await #expect(throws: APIError.shaChanged) {
    try await service.prepare(PRKey(testLink), displayedHead: headA)
  }
  await mock.configure(heads: [headA, headB])
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  await #expect(throws: APIError.shaChanged) {
    try await service.approve(confirmation, explicitlyConfirmed: true)
  }
  #expect(await mock.puts == 0)
}
@Test(arguments: ["completed", "abandoned", "draft", "approved"]) func blockedApproval(
  _ state: String
) async throws {
  let (_, service, mock, _) = try setup()
  await mock.configure(
    status: state == "completed" || state == "abandoned" ? state : "active",
    draft: state == "draft", vote: state == "approved" ? 10 : 0)
  let expected: APIError =
    state == "draft" ? .draft : state == "approved" ? .alreadyApproved : .inactive
  await #expect(throws: expected) {
    try await service.prepare(PRKey(testLink), displayedHead: headA)
  }
  #expect(await mock.puts == 0)
}
@Test(arguments: [401, 403, 404, 429, 500]) func httpFailuresAreNotCached(_ code: Int) async throws
{
  let (_, service, mock, _) = try setup()
  await mock.configure(statusCode: code)
  let expected: APIError = [
    401: .unauthorized, 403: .forbidden, 404: .notFound, 429: .rateLimited, 500: .server,
  ][code]!
  await #expect(throws: expected) { try await service.snapshot(PRKey(testLink)) }
  await mock.configure(statusCode: 200)
  #expect(try await service.snapshot(PRKey(testLink)).pr.pullRequestId == 42)
  #expect(await mock.count == 4)
}
@Test func timeoutAndSanitizedErrors() async throws {
  let (client, _, mock, _) = try setup()
  await mock.configure(transportError: URLError(.timedOut))
  await #expect(throws: APIError.timeout) { try await client.snapshot(PRKey(testLink)) }
  #expect(!APIError.unauthorized.localizedDescription.contains("synthetic"))
  let noPAT = AzureDevOpsClient(credentials: MemoryCredentials(), transport: mock)
  await #expect(throws: APIError.missingPAT) { try await noPAT.snapshot(PRKey(testLink)) }
}
@Test func confirmationExpirationAndCredentialChange() async throws {
  let (_, service, mock, clock) = try setup()
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  clock.advance(121)
  await #expect(throws: APIError.unconfirmed) {
    try await service.approve(confirmation, explicitlyConfirmed: true)
  }
  let next = try await service.prepare(PRKey(testLink), displayedHead: headA)
  await service.invalidate()
  await #expect(throws: APIError.unconfirmed) {
    try await service.approve(next, explicitlyConfirmed: true)
  }
  #expect(await mock.puts == 0)
}
@Test func noApprovedWithoutServerRefreshAndNoMutationRetry() async throws {
  let (_, service, mock, _) = try setup()
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  await mock.configure(failRefresh: true)
  await #expect(throws: APIError.unverified) {
    try await service.approve(confirmation, explicitlyConfirmed: true)
  }
  #expect(await mock.puts == 1)
}
@Test func newCommitsRequireTrustedHistory() async throws {
  let (_, service, mock, _) = try setup()
  let hovered = try await service.snapshot(PRKey(testLink))
  #expect(await service.state(PRKey(testLink), snapshot: hovered) == .approve)
  await mock.configure(heads: [headB])
  let newer = try await service.snapshot(PRKey(testLink), force: true)
  #expect(await service.state(PRKey(testLink), snapshot: newer) == .approve)
  await mock.configure(heads: [headA])
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  _ = try await service.approve(confirmation, explicitlyConfirmed: true)
  await mock.configure(heads: [headB])
  let updated = try await service.snapshot(PRKey(testLink), force: true)
  #expect(await service.state(PRKey(testLink), snapshot: updated) == .newCommits)
}

@Test func finalSHARevalidationBeforePUT() async throws {
  let (_, service, mock, _) = try setup()
  await mock.configure(heads: [headA, headA, headB])
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  await #expect(throws: APIError.shaChanged) {
    try await service.approve(confirmation, explicitlyConfirmed: true)
  }
  #expect(await mock.puts == 0)
}

@Test(.enabled(if: ProcessInfo.processInfo.environment["PRHOVER_KEYCHAIN_TEST"] == "1"))
func realKeychainSyntheticRoundTrip() throws {
  let store = KeychainCredentialStore(service: "com.linkpeek.tests." + UUID().uuidString)
  defer { try? store.remove(organization: "synthetic-org") }
  #expect(try store.read(organization: "synthetic-org") == nil)
  try store.save("synthetic-token-one", organization: "synthetic-org")
  #expect(try store.read(organization: "synthetic-org") == "synthetic-token-one")
  #expect(try store.contains(organization: "synthetic-org"))
  try store.save("synthetic-token-two", organization: "synthetic-org")
  #expect(try store.read(organization: "synthetic-org") == "synthetic-token-two")
  try store.remove(organization: "synthetic-org")
  #expect(try store.read(organization: "synthetic-org") == nil)
}

@Test func identityChangeRejectsApproval() async throws {
  let (_, service, mock, _) = try setup()
  let confirmation = try await service.prepare(PRKey(testLink), displayedHead: headA)
  await mock.configure(identity: repositoryID)
  await #expect(throws: APIError.identity) {
    try await service.approve(confirmation, explicitlyConfirmed: true)
  }
  #expect(await mock.puts == 0)
}

@Test func hoverRefreshSeesExternalVoteResetWithinCacheTTL() async throws {
  let (_, service, mock, _) = try setup()
  await mock.configure(vote: 10)
  let approved = try await service.snapshot(PRKey(testLink))
  #expect(approved.approved)
  await mock.configure(vote: 0)
  let refreshed = try await service.snapshotForHover(PRKey(testLink))
  #expect(!refreshed.approved)
  #expect(await service.state(PRKey(testLink), snapshot: refreshed) == .approve)
  #expect(await mock.puts == 0)
}

@Test func quickApprovalRequiresOptInAndRevalidatesHead() async throws {
  let (_, service, mock, _) = try setup()
  await #expect(throws: APIError.unconfirmed) {
    try await service.quickApprove(PRKey(testLink), displayedHead: headA, enabledByUser: false)
  }
  #expect(await mock.count == 0)
  await mock.configure(heads: [headB])
  await #expect(throws: APIError.shaChanged) {
    try await service.quickApprove(PRKey(testLink), displayedHead: headA, enabledByUser: true)
  }
  #expect(await mock.puts == 0)
}

@Test func quickApprovalVerifiesSingleVote() async throws {
  let (_, service, mock, _) = try setup()
  let result = try await service.quickApprove(
    PRKey(testLink), displayedHead: headA, enabledByUser: true)
  #expect(result.vote == 10)
  #expect(await mock.puts == 1)
}

@Test func tooltipMissDoesNotCancelExistingDwell() {
  var gate = HoverGate()
  _ = gate.observe(PRKey(testLink), at: 0, enabled: true)
  #expect(gate.shouldResetOnMissingTarget(pointerInsideAnchor: true))
  _ = gate.start(at: 1, delay: 0.5)
  #expect(!gate.shouldResetOnMissingTarget(pointerInsideAnchor: true))
  #expect(gate.shouldResetOnMissingTarget(pointerInsideAnchor: false))
  gate.reset()
  #expect(gate.shouldResetOnMissingTarget(pointerInsideAnchor: true))
}

@Test func exactReviewerVoteAndLifecycleStates() async throws {
  let (_, service, mock, _) = try setup()
  for (vote, state) in [
    (0, PRDisplayState.approve), (10, .approved),
    (5, .approvedWithSuggestions), (-5, .waitingForAuthor), (-10, .rejected),
  ] {
    await mock.configure(vote: vote)
    let snapshot = try await service.snapshotForHover(PRKey(testLink))
    #expect(await service.state(PRKey(testLink), snapshot: snapshot) == state)
    #expect(snapshot.eligible == [0, -5, -10].contains(vote))
    #expect(snapshot.statusCaption.contains("Your vote:"))
  }
  for (status, state) in [
    ("completed", PRDisplayState.completed), ("abandoned", .abandoned),
    ("unknown", .unavailable),
  ] {
    await mock.configure(status: status, vote: -10)
    let snapshot = try await service.snapshotForHover(PRKey(testLink))
    #expect(await service.state(PRKey(testLink), snapshot: snapshot) == state)
    #expect(!snapshot.eligible)
  }
}
