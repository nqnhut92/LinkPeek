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
    public static bool KeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static uint ForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid;
    }
}

internal static class LinkInspector
{
    // Called from an MTA worker, never the UI thread. A single outstanding worker
    // bounds resource use even if a third-party UIA provider stops responding.
    public static Inspection? Inspect(Point point, uint foreground)
    {
        try
        {
            if (foreground == Environment.ProcessId || foreground == 0) return null;
            var hit = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            if (hit is null || hit.Current.ProcessId != foreground) return null;
            // Older Win32 providers expose MSAA values but no UIA ValuePattern.
            if (ReadMsaa(point, foreground) is { } accessibleLink) return accessibleLink;
            var pending = new Queue<(AutomationElement Node, int Depth)>();
            var visited = new HashSet<string>();
            var deadline = Stopwatch.StartNew();
            pending.Enqueue((hit, 0));
            var walker = TreeWalker.RawViewWalker;
            for (var count = 0; pending.Count > 0 && count < 24 && deadline.ElapsedMilliseconds < 350; count++)
            {
                var (node, depth) = pending.Dequeue();
                if (!visited.Add(string.Join(".", node.GetRuntimeId()))) continue;
                var current = node.Current;
                if (current.ProcessId != foreground || current.IsOffscreen || current.IsPassword) continue;
                var rect = current.BoundingRectangle;
                if (rect.IsEmpty || !rect.Contains(point.X, point.Y)) continue;
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
                        if (WebLink.Parse(candidate, accessibilityHyperlink: true) is { } link)
                            return new(link, Rectangle.FromLTRB((int)rect.Left, (int)rect.Top,
                                (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom)), foreground);
                    }
                }
                if (depth >= 3) continue;
                if (walker.GetParent(node) is { } parent) pending.Enqueue((parent, depth + 1));
                var child = walker.GetFirstChild(node);
                for (var i = 0; child is not null && i < 8; i++)
                {
                    if (child.Current.BoundingRectangle.Contains(point.X, point.Y)) pending.Enqueue((child, depth + 1));
                    child = walker.GetNextSibling(child);
                }
            }
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or
            UnauthorizedAccessException or COMException) { /* Providers may disappear between calls. */ }
        return null;
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromPoint(Point point,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible,
        [MarshalAs(UnmanagedType.Struct)] out object childId);

    private static Inspection? ReadMsaa(Point point, uint foreground)
    {
        if (AccessibleObjectFromPoint(point, out var accessible, out var child) != 0 || accessible is null) return null;
        try
        {
            if (Convert.ToInt32(accessible.get_accRole(child)) != 0x1e) return null; // ROLE_SYSTEM_LINK
            accessible.accLocation(out var x, out var y, out var width, out var height, child);
            var bounds = new Rectangle(x, y, width, height);
            if (!bounds.Contains(point) || width > 2400 || height > 180) return null;
            var link = WebLink.Parse(accessible.get_accValue(child), true) ?? WebLink.Parse(accessible.get_accName(child), true);
            return link is null ? null : new Inspection(link, bounds, foreground);
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidCastException) { return null; }
        finally { if (Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
    }
}
