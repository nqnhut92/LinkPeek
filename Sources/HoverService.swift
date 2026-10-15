import Foundation

struct ApprovalConfirmation: Sendable, Equatable {
  let token: UUID
  let key: PRKey
  let snapshot: PRSnapshot
  let created: Date
  let generation: Int
}

enum PRDisplayState: String, Sendable {
  case approve = "✓ Approve"
  case approved = "Approved"
  case approvedWithSuggestions = "Approved with suggestions"
  case waitingForAuthor = "Waiting for author"
  case rejected = "Rejected"
  case abandoned = "Abandoned"
  case unavailable = "Unavailable"
  var canApprove: Bool { self == .approve || self == .waitingForAuthor || self == .rejected }
  case loading = "Loading…"
  case newCommits = "New Commits"
  case completed = "Completed"
  case draft = "Draft"
  case error = "Error"
  case configure = "Configure PAT"
  static func resolve(_ snapshot: PRSnapshot, reviewedHead: String?) -> Self {
    if snapshot.pr.status == "completed" { return .completed }
    if snapshot.pr.status == "abandoned" { return .abandoned }
    if snapshot.pr.status != "active" { return .unavailable }
    if snapshot.pr.isDraft == true { return .draft }
    if let reviewedHead, reviewedHead != snapshot.pr.head { return .newCommits }
    switch snapshot.vote {
    case 10: return .approved
    case 5: return .approvedWithSuggestions
    case -5: return .waitingForAuthor
    case -10: return .rejected
    case 0: return .approve
    default: return .unavailable
    }
  }
}

actor HoverService {
  private struct Entry {
    let value: PRSnapshot
    let expires: Date
  }
  private let client: AzureDevOpsClient
  private let now: @Sendable () -> Date
  private var cache: [PRKey: Entry] = [:]
  private var pending: [PRKey: (UUID, Task<PRSnapshot, Error>)] = [:]
  private var reviewed: [PRKey: (String, String)] = [:]
  private var confirmations: [UUID: ApprovalConfirmation] = [:]
  private var mutating: Set<PRKey> = []
  private var generation = 0
  init(client: AzureDevOpsClient, now: @escaping @Sendable () -> Date = { Date() }) {
    self.client = client
    self.now = now
  }
  func invalidate() {
    generation += 1
    cache.removeAll()
    reviewed.removeAll()
    confirmations.removeAll()
    for (_, task) in pending.values { task.cancel() }
    pending.removeAll()
  }
  func snapshot(_ key: PRKey, force: Bool = false) async throws -> PRSnapshot {
    if !force, let entry = cache[key], entry.expires > now() { return entry.value }
    if !force, let (_, task) = pending[key] { return try await task.value }
    let epoch = generation
    let task = Task { try await client.snapshot(key) }
    let requestID = UUID()
    if !force { pending[key] = (requestID, task) }
    do {
      let value = try await task.value
      guard epoch == generation else { throw CancellationError() }
      if !force, pending[key]?.0 == requestID { pending[key] = nil }
      cache[key] = Entry(value: value, expires: now().addingTimeInterval(60))
      return value
    } catch {
      if !force, pending[key]?.0 == requestID { pending[key] = nil }
      throw error
    }
  }
  /// Each new dwell checks the server, including external vote resets within the cache TTL.
  func snapshotForHover(_ key: PRKey) async throws -> PRSnapshot {
    try await snapshot(key, force: true)
  }
  func state(_ key: PRKey, snapshot: PRSnapshot) -> PRDisplayState {
    let previous = reviewed[key]
    return .resolve(snapshot, reviewedHead: previous?.0 == snapshot.reviewerID ? previous?.1 : nil)
  }
  private func validate(_ snapshot: PRSnapshot, expectedHead: String) throws {
    guard snapshot.pr.status == "active" else { throw APIError.inactive }
    guard snapshot.pr.isDraft != true else { throw APIError.draft }
    guard snapshot.pr.head == expectedHead else { throw APIError.shaChanged }
    guard !snapshot.approved else { throw APIError.alreadyApproved }
  }
  func prepare(_ key: PRKey, displayedHead: String) async throws -> ApprovalConfirmation {
    let epoch = generation
    let latest = try await snapshot(key, force: true)
    guard epoch == generation else { throw CancellationError() }
    try validate(latest, expectedHead: displayedHead)
    let confirmation = ApprovalConfirmation(
      token: UUID(), key: key, snapshot: latest, created: now(), generation: epoch)
    confirmations[confirmation.token] = confirmation
    return confirmation
  }
  /// The user's explicit Approve click authorizes this path only after opt-in.
  func quickApprove(_ key: PRKey, displayedHead: String, enabledByUser: Bool) async throws
    -> PRSnapshot
  {
    guard enabledByUser else { throw APIError.unconfirmed }
    let prepared = try await prepare(key, displayedHead: displayedHead)
    return try await approve(prepared, explicitlyConfirmed: true)
  }
  func discard(_ confirmation: ApprovalConfirmation) { confirmations[confirmation.token] = nil }
  func approve(_ confirmation: ApprovalConfirmation, explicitlyConfirmed: Bool) async throws
    -> PRSnapshot
  {
    guard explicitlyConfirmed,
      confirmations.removeValue(forKey: confirmation.token) == confirmation,
      confirmation.generation == generation, now().timeIntervalSince(confirmation.created) <= 120
    else { throw APIError.unconfirmed }
    let key = confirmation.key
    guard mutating.insert(key).inserted else { throw APIError.busy }
    defer {
      mutating.remove(key)
      cache[key] = nil
    }
    let latest = try await snapshot(key, force: true)
    try validate(latest, expectedHead: confirmation.snapshot.pr.head!)
    guard latest.reviewerID == confirmation.snapshot.reviewerID,
      generation == confirmation.generation
    else { throw APIError.identity }
    try Task.checkCancellation()
    // ADO reviewers API has no atomic head-SHA precondition. This is the last read
    // immediately before the single PUT; never automatically retry this mutation.
    let response: Reviewer
    do {
      response = try await client.vote(
        key, reviewerID: latest.reviewerID, expectedHead: latest.pr.head!)
    } catch let error as APIError {
      if [.timeout, .network, .malformed, .server].contains(error) { throw APIError.unverified }
      throw error
    }
    guard response.id.lowercased() == latest.reviewerID, response.vote == 10 else {
      throw APIError.unverified
    }
    let refreshed: PRSnapshot
    do { refreshed = try await snapshot(key, force: true) } catch { throw APIError.unverified }
    guard refreshed.reviewerID == latest.reviewerID, refreshed.vote == 10,
      refreshed.pr.head == latest.pr.head, refreshed.pr.status == "active",
      refreshed.pr.isDraft != true,
      generation == confirmation.generation
    else { throw APIError.unverified }
    reviewed[key] = (latest.reviewerID, latest.pr.head!)
    return refreshed
  }
}

// Pure hover gate and generation ticket: deterministic clocks make delay and
// stale-response rejection testable without mouse events or network access.
typealias HoverGate = DwellGate<PRKey>

struct DwellGate<Key: Equatable> {
  private(set) var key: Key?
  private(set) var entered: TimeInterval = 0
  private(set) var ticket = UUID()
  private(set) var started = false
  mutating func observe(_ newKey: Key?, at time: TimeInterval, enabled: Bool) -> Bool {
    let target = enabled ? newKey : nil
    if target != key {
      key = target
      entered = time
      ticket = UUID()
      started = false
    }
    return target != nil
  }
  mutating func start(at time: TimeInterval, delay: TimeInterval) -> UUID? {
    guard key != nil, !started, time - entered >= delay else { return nil }
    started = true
    return ticket
  }
  func accepts(_ responseTicket: UUID, key: Key) -> Bool {
    ticket == responseTicket && self.key == key
  }
  func shouldResetOnMissingTarget(pointerInsideAnchor: Bool) -> Bool {
    !started || !pointerInsideAnchor
  }
  mutating func reset() {
    key = nil
    ticket = UUID()
    started = false
  }
}
