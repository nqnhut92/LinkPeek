import AppKit
import Testing

@testable import LinkPeek

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
