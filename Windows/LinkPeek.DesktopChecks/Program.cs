using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LinkPeek.Windows;

internal static class Program
{
    private sealed record Host(long Window, uint Process);
    private sealed record Targets(int LinkX, int LinkY, int TextX, int TextY, uint RendererProcess);
    private const string SampleUrl = "https://example.org/hosted-renderer";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args is ["--host", var directory]) { RunHost(directory); return 0; }
            if (args is ["--renderer", var parent, var dir])
            { RunRenderer(new IntPtr(long.Parse(parent, CultureInfo.InvariantCulture)), dir); return 0; }
            var output = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-previews");
            Directory.CreateDirectory(output);
            VerifyHostedHyperlink(Path.Combine(output, "hosted-link-check.txt"));
            Console.WriteLine("PASS: cross-process hosted hyperlink, plain-text rejection and foreign-window rejection.");
            var previews = UiPreview.Render(output);
            if (previews.Count == 0 || previews.Any(file => !File.Exists(file))) throw new Exception("UI previews were not written.");
            Console.WriteLine($"PASS: {previews.Count} light/dark previews at 100%, 150% and 200% scale; layout audit found no clipped or overflowing controls.");
            UiPreview.VerifySavedPatMask(output);
            Console.WriteLine("PASS: saved-PAT mask aligns with typed glyphs, uses placeholder tone, keeps the input empty and hides on typing.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine("::error::" + error.GetBaseException().Message.Replace('\n', ' ').Replace('\r', ' '));
            return 1;
        }
    }

    private static Process Spawn(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new Exception("Cannot start accessibility fixture.");
    }

    private static void VerifyHostedHyperlink(string log)
    {
        var directory = Path.Combine(Path.GetTempPath(), "linkpeek-uia-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        using var host = Spawn("--host", directory);
        var lines = new ConcurrentQueue<string>();
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(directory, "targets.json")) && deadline.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (host.HasExited) throw new Exception("Fixture host exited before it was ready.");
                Thread.Sleep(50);
            }
            var info = JsonSerializer.Deserialize<Host>(File.ReadAllText(Path.Combine(directory, "host.json")))!;
            var targets = JsonSerializer.Deserialize<Targets>(File.ReadAllText(Path.Combine(directory, "targets.json")))!;
            if (targets.RendererProcess == info.Process) throw new Exception("Fixture must use distinct host and renderer processes.");
            var window = new IntPtr(info.Window);
            var linkPoint = new Point(targets.LinkX, targets.LinkY);
            var check = Task.Run(() =>
            {
                var link = LinkInspector.Inspect(linkPoint, info.Process, window, lines.Enqueue);
                if (link?.Link.Text != SampleUrl) throw new Exception("Hosted renderer hyperlink was not resolved. " + string.Join(" | ", lines));
                var text = LinkInspector.Inspect(new Point(targets.TextX, targets.TextY), info.Process, window, lines.Enqueue);
                if (text is not null) throw new Exception("Plain URL text must not be treated as a hyperlink.");
                if (Native.SameRoot(Native.WindowFromPoint(linkPoint), IntPtr.Zero)) throw new Exception("Invalid host must be rejected.");
                var foreign = LinkInspector.Inspect(linkPoint, info.Process, GetDesktopWindow(), lines.Enqueue);
                if (foreign is not null) throw new Exception("A foreign host window must not be accepted.");
            });
            if (!check.Wait(TimeSpan.FromSeconds(20))) throw new Exception("Accessibility fixture timed out.");
        }
        finally
        {
            File.WriteAllLines(log, lines.ToArray());
            if (!host.HasExited) host.Kill(entireProcessTree: true);
            host.WaitForExit(5000);
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private static void AtomicJson<T>(string file, T value)
    {
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(value)); File.Move(file + ".tmp", file, true);
    }

    private static void RunHost(string directory)
    {
        using var form = new Form { Text = "LinkPeek synthetic host", StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80), ClientSize = new Size(520, 180), TopMost = true };
        Process? renderer = null;
        form.Shown += (_, _) =>
        {
            AtomicJson(Path.Combine(directory, "host.json"), new Host(form.Handle.ToInt64(), (uint)Environment.ProcessId));
            renderer = Spawn("--renderer", form.Handle.ToInt64().ToString(CultureInfo.InvariantCulture), directory);
        };
        form.FormClosed += (_, _) => { if (renderer is { HasExited: false }) renderer.Kill(true); renderer?.Dispose(); };
        Application.Run(form);
    }

    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

    private static void RunRenderer(IntPtr parent, string directory)
    {
        using var form = new Form { Text = "Synthetic renderer", FormBorderStyle = FormBorderStyle.None,
            ClientSize = new Size(480, 130), BackColor = Color.White };
        var link = new LinkLabel { Text = SampleUrl, AccessibleName = SampleUrl,
            AccessibleRole = AccessibleRole.Link, AutoSize = true, Location = new Point(16, 20) };
        var text = new Label { Text = SampleUrl, AccessibleName = SampleUrl,
            AccessibleRole = AccessibleRole.StaticText, AutoSize = true, Location = new Point(16, 70) };
        form.Controls.Add(link); form.Controls.Add(text);
        form.Shown += (_, _) =>
        {
            SetWindowLong(form.Handle, -16, (GetWindowLong(form.Handle, -16) & ~unchecked((int)0x80000000)) | 0x40000000);
            SetParent(form.Handle, parent); MoveWindow(form.Handle, 12, 12, 480, 130, true);
            Application.DoEvents();
            var linkPoint = link.PointToScreen(new Point(link.Width / 2, link.Height / 2));
            var textPoint = text.PointToScreen(new Point(text.Width / 2, text.Height / 2));
            AtomicJson(Path.Combine(directory, "targets.json"), new Targets(linkPoint.X, linkPoint.Y, textPoint.X, textPoint.Y,
                (uint)Environment.ProcessId));
        };
        Application.Run(form);
    }
}
