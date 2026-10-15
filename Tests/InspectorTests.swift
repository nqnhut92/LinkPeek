import AppKit
import Testing

@testable import LinkPeek

@Test(arguments: [
  "https://teams.public.onecdn.static.microsoft/evergreen-assets/emoji/laugh.png",
  "https://tflteams.public.onecdn.static.microsoft/evergreen-assets/emoji/laugh.png",
  "https://example.com/emoji.svg",
])
func imageSourcesAreNotHoverTargets(_ source: String) {
  #expect(Inspector.linkCandidate(urlText: source, value: nil, role: "AXImage") == nil)
  #expect(Inspector.linkCandidate(urlText: source, value: source, role: "AXImage") == nil)
  // An actual hyperlink to an image remains valid, regardless of its host or extension.
  #expect(
    Inspector.linkCandidate(urlText: source, value: nil, role: "AXLink")?.url.absoluteString
      == source)
}

@Test func linkedImageUsesEnclosingLinkDestination() {
  let source = "https://example.com/icon.png"
  let destination = "https://example.com/article"
  let candidates = [("AXImage", source), ("AXLink", destination)]
  let selected = candidates.lazy.compactMap {
    Inspector.linkCandidate(urlText: $0.1, value: nil, role: $0.0)
  }.first
  #expect(selected?.url.absoluteString == destination)
  #expect(Inspector.linkCandidate(urlText: nil, value: destination, role: "AXLink") != nil)
  #expect(Inspector.linkCandidate(urlText: nil, value: destination, role: "AXStaticText") == nil)
}

@Test func backgroundInspectionRunsAXWorkAndCompletionOnMainQueue() async {
  let point = CGPoint(x: 10, y: 20)
  let inspector = Inspector { receivedPoint, pid, debug in
    MainActor.assertIsolated()
    dispatchPrecondition(condition: .onQueue(.main))
    #expect(receivedPoint == point)
    #expect(pid == 123)
    #expect(debug)
    return Inspection(frame: CGRect(x: 1, y: 2, width: 3, height: 4), diagnostics: ["checked"])
  }
  let result = await withCheckedContinuation { continuation in
    DispatchQueue.global().async {
      dispatchPrecondition(condition: .notOnQueue(.main))
      inspector.inspect(point: point, pid: 123, debug: true) { result in
        dispatchPrecondition(condition: .onQueue(.main))
        continuation.resume(returning: result)
      }
    }
  }
  #expect(result.frame == CGRect(x: 1, y: 2, width: 3, height: 4))
  #expect(result.diagnostics == ["checked"])
}
