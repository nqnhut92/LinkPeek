using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LinkPeek.Windows;

internal sealed record Inspection(WebLink Link, Rectangle Bounds, uint ProcessId);

internal static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    public static bool KeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static uint ForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid;
    }

    public static bool SameRoot(IntPtr first, IntPtr second) => first != IntPtr.Zero && second != IntPtr.Zero &&
        GetAncestor(first, 2) == GetAncestor(second, 2);
}

internal static class LinkInspector
{
    // Called from an MTA worker, never the UI thread. A single outstanding worker
    // bounds resource use even if a third-party UIA provider stops responding.
    public static Inspection? Inspect(Point point, uint foreground, IntPtr foregroundWindow = default, Action<string>? trace = null)
    {
        try
        {
            if (foreground == Environment.ProcessId || foreground == 0) return null;
            if (foregroundWindow == IntPtr.Zero) foregroundWindow = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(foregroundWindow, out var windowProcess);
            var sameRoot = Native.SameRoot(Native.WindowFromPoint(point), foregroundWindow);
            trace?.Invoke($"Foreground PID: {foreground}; pointer in foreground root: {sameRoot}");
            if (windowProcess != foreground || !sameRoot) return null;
            var hit = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            if (hit is null) return null;
            trace?.Invoke($"UIA hit PID differs from foreground: {hit.Current.ProcessId != foreground}");
            // WebView2/Chromium can expose elements from a renderer process. Scope
            // inspection by the actual top-level HWND under the pointer, not PID.
            var pending = new LinkedList<(AutomationElement Node, int Depth)>();
            var visited = new HashSet<string>();
            var deadline = Stopwatch.StartNew();
            var walker = TreeWalker.RawViewWalker;
            pending.AddFirst((hit, 0));
            for (var count = 0; pending.Count > 0 && count < 24 && deadline.ElapsedMilliseconds < 350; count++)
            {
                var (node, depth) = pending.First!.Value; pending.RemoveFirst();
                if (!visited.Add(string.Join(".", node.GetRuntimeId()))) continue;
                var current = node.Current;
                if (current.IsOffscreen || current.IsPassword) continue;
                var nativeHandle = new IntPtr(current.NativeWindowHandle);
                if (nativeHandle != IntPtr.Zero && !Native.SameRoot(nativeHandle, foregroundWindow)) continue;
                var rect = current.BoundingRectangle;
                if (rect.IsEmpty || !rect.Contains(point.X, point.Y)) continue;
                trace?.Invoke($"Node {count}: role={current.ControlType.ProgrammaticName}; rendererPID={current.ProcessId != foreground}; " +
                    $"bounds={(int)rect.Width}x{(int)rect.Height}; name={Describe(current.Name)}");
                if (current.ControlType == ControlType.Hyperlink && rect.Width <= 2400 && rect.Height <= 180)
                {
                    var candidates = new List<string?>();
                    if (node.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
                        candidates.Add(((ValuePattern)value).Current.Value);
                    // Name is accepted only when it is a complete URL on a hyperlink.
                    // Never scrape message text, invoke a link or inspect clipboard data.
                    candidates.Add(current.Name);
                    foreach (var candidate in candidates)
                    {
                        trace?.Invoke($"Hyperlink candidate: {Describe(candidate)}");
                        if (WebLink.Parse(candidate, accessibilityHyperlink: true) is { } link)
                        {
                            trace?.Invoke("Resolved hyperlink through UI Automation.");
                            return new(link, Rectangle.FromLTRB((int)rect.Left, (int)rect.Top,
                                (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom)), foreground);
                        }
                    }
                }
                // Inspect each ancestor before expanding siblings/descendants.
                if (depth < 8 && walker.GetParent(node) is { } parent) pending.AddFirst((parent, depth + 1));
                if (depth >= 3) continue;
                var child = walker.GetFirstChild(node);
                for (var i = 0; child is not null && i < 8; i++)
                {
                    if (child.Current.BoundingRectangle.Contains(point.X, point.Y)) pending.AddLast((child, depth + 1));
                    child = walker.GetNextSibling(child);
                }
            }
            if (ReadMsaa(point, foreground, trace) is { } accessibleLink) return accessibleLink;
            trace?.Invoke("No complete web target exposed by the bounded hyperlink inspection.");
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or
            UnauthorizedAccessException or COMException) { trace?.Invoke($"Provider unavailable: {e.GetType().Name}"); }
        return null;
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromPoint(Point point,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible,
        [MarshalAs(UnmanagedType.Struct)] out object childId);

    private static string Describe(string? value) => string.IsNullOrEmpty(value) ? "empty" :
        $"{value.Length} chars, completeWebTarget={WebLink.Parse(value, true) is not null}";

    private static Inspection? ReadMsaa(Point point, uint foreground, Action<string>? trace)
    {
        if (AccessibleObjectFromPoint(point, out var accessible, out var child) != 0 || accessible is null) return null;
        var objects = new List<object> { accessible };
        try
        {
            for (var depth = 0; depth <= 8; depth++)
            {
                foreach (var id in new object[] { child, 0 }.Distinct())
                {
                    var role = Convert.ToInt32(accessible.get_accRole(id));
                    trace?.Invoke($"MSAA ancestor {depth}: role={role}");
                    if (role != 0x1e) continue; // ROLE_SYSTEM_LINK
                    accessible.accLocation(out var x, out var y, out var width, out var height, id);
                    var bounds = new Rectangle(x, y, width, height);
                    if (!bounds.Contains(point) || width > 2400 || height > 180) continue;
                    var link = WebLink.Parse(accessible.get_accValue(id), true) ?? WebLink.Parse(accessible.get_accName(id), true);
                    if (link is not null)
                    { trace?.Invoke("Resolved hyperlink through MSAA."); return new(link, bounds, foreground); }
                }
                if (accessible.accParent is not Accessibility.IAccessible parent || objects.Contains(parent)) break;
                accessible = parent; objects.Add(parent); child = 0;
            }
            return null;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidCastException) { return null; }
        finally { foreach (var obj in objects) if (Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    }
}
