using System.Diagnostics;

namespace LinkPeek.Windows;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 120 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Preferences preferences = Preferences.Load();
    private readonly DwellGate gate = new();
    private CardForm? card;
    private SettingsForm? settings;
    private Task<Inspection?>? inspection;
    private Point inspectedPoint;
    private uint inspectedProcess;
    private Rectangle anchor;
    private uint anchorProcess;
    private string? suppressed;
    private bool paused;
    private bool diagnosing;
    private TimeSpan outsideSince;
    private TimeSpan captureUntil;
    private bool snipKeyWasDown;
    private AzureDevOpsClient? client;
    private ApprovalService? approvals;

    public TrayApplication()
    {
        using var stream = typeof(TrayApplication).Assembly.GetManifestResourceStream("LinkPeek.Windows.AppIcon.png");
        if (stream is not null)
        {
            using var image = Image.FromStream(stream);
            using var bitmap = new Bitmap(image, new Size(32, 32));
            var handle = bitmap.GetHicon();
            try { using var borrowed = Icon.FromHandle(handle); icon = (Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }
        else icon = (Icon)SystemIcons.Application.Clone();
        var menuColors = Palette.Current;
        var menu = new ContextMenuStrip { BackColor = menuColors.Surface, ForeColor = menuColors.Text,
            Font = UI.TextFont(), Padding = new Padding(4), Renderer = new ToolStripProfessionalRenderer(new MenuColors(menuColors)) };
        menu.Items.Add("Open link…", null, (_, _) => OpenLink());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Diagnose hovered link…", null, async (_, _) => await DiagnoseHoverAsync());
        var pause = new ToolStripMenuItem("Pause hover") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => { paused = pause.Checked; CloseCard(); gate.Reset(); }; menu.Items.Add(pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About LinkPeek", null, (_, _) => MessageBox.Show(
            $"LinkPeek for Windows {SettingsForm.AppVersion}\nOffline QR codes and Azure DevOps PR previews.\nMIT License · See LICENSE alongside the executable.", "LinkPeek"));
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "LinkPeek", Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => OpenSettings();
        ReloadClient();
        timer.Tick += (_, _) => Tick(); timer.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private void ReloadClient()
    {
        CloseCard(); gate.Reset(); approvals?.Invalidate(); client?.Dispose(); client = null; approvals = null;
        if (!PullRequestLink.ValidOrganization(preferences.Organization)) return;
        try
        {
            var token = CredentialStore.Load(preferences.Organization);
            if (token is null) return;
            client = new AzureDevOpsClient(preferences.Organization, token);
            approvals = new ApprovalService(client);
        }
        catch { /* QR works without credentials. PR card directs users to Settings. */ }
    }

    private void OpenSettings()
    {
        CloseCard(); gate.Reset();
        if (settings is { IsDisposed: false }) { settings.Activate(); return; }
        settings = new SettingsForm(preferences, ReloadClient); settings.Show();
    }

    private void OpenLink()
    {
        CloseCard(); gate.Reset();
        using var dialog = new ModernForm { Text = "LinkPeek · Open link", ClientSize = new Size(540, 170),
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 10),
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var label = UI.Label("Paste a complete HTTP or HTTPS URL", 11, bold: true); label.Location = new Point(24, 20);
        var input = new TextBox { Location = new Point(24, 57), Width = 490,
            BackColor = Palette.Current.Surface, ForeColor = Palette.Current.Text };
        var open = UI.Button("Open link", () => { }, true); open.Location = new Point(399, 110); open.DialogResult = DialogResult.OK;
        dialog.Controls.AddRange([label, input, open]); dialog.AcceptButton = open;
        if (dialog.ShowDialog() != DialogResult.OK) return;
        if (WebLink.Parse(input.Text.Trim()) is not { } link)
        { MessageBox.Show("Enter a complete HTTP or HTTPS URL without embedded credentials.", "LinkPeek"); return; }
        ShowCard(link, Rectangle.Empty, Cursor.Position, true);
    }

    private async Task DiagnoseHoverAsync()
    {
        if (diagnosing) return;
        MessageBox.Show("After clicking OK, place the pointer over the problem link and keep it still.\n\n" +
            "LinkPeek will inspect it after 3 seconds. The report contains only accessibility roles, process relationships and URL-presence flags. No chat text, URL values or credentials are recorded.",
            "Diagnose hovered link", MessageBoxButtons.OK, MessageBoxIcon.Information);
        diagnosing = true; CloseCard(); gate.Reset();
        var report = "";
        try
        {
            await Task.Delay(3000);
            if (inspection is { IsCompleted: false }) await Task.WhenAny(inspection, Task.Delay(2000));
            if (inspection is { IsCompleted: false })
                report = "An existing accessibility request has not returned. Restart LinkPeek and try Diagnose again.\nThe provider may be stalled; no additional worker was started.";
            else
            {
                var lines = new List<string> { $"LinkPeek Windows {SettingsForm.AppVersion}",
                    $"Windows: {Environment.OSVersion.Version}", "No URL values, labels or chat contents are included.", "" };
                var point = Cursor.Position; var window = Native.GetForegroundWindow();
                Native.GetWindowThreadProcessId(window, out var pid);
                inspectedPoint = point; inspectedProcess = pid;
                inspection = Task.Run(() => LinkInspector.Inspect(point, pid, window, lines.Add));
                if (await Task.WhenAny(inspection, Task.Delay(2500)) != inspection)
                    report = "The app's accessibility provider did not respond within 2.5 seconds.\nNo additional worker was started. Restart LinkPeek if automatic hover remains stalled.";
                else { await inspection; report = string.Join(Environment.NewLine, lines); }
            }
        }
        catch { report = "Accessibility inspection failed. No content was recorded."; }
        finally { diagnosing = false; }
        new DiagnosticForm(report).Show();
    }

    private void CloseCard()
    {
        var old = card; card = null; old?.Close();
    }

    private void ShowCard(WebLink link, Rectangle bounds, Point point, bool manual)
    {
        CloseCard(); anchor = bounds; anchorProcess = Native.ForegroundProcess(); outsideSince = TimeSpan.Zero;
        captureUntil = TimeSpan.Zero;
        var current = new CardForm(link, client, approvals, preferences with { }, manual,
            () => suppressed = link.Text,
            () => tray.ShowBalloonTip(3000, "LinkPeek", "Azure DevOps verified your approval.", ToolTipIcon.Info));
        card = current;
        current.FormClosed += (_, _) => { if (card == current) { card = null; gate.Reset(); } };
        current.Place(bounds, point); current.Show();
    }

    private void Tick()
    {
        if (Native.KeyDown(0x1B) && card is not null && !card.Busy) { CloseCard(); gate.Reset(); }
        // Hold a card while Win+Shift+S is being used. No screenshot pixels are read.
        var snip = (Native.KeyDown(0x5B) || Native.KeyDown(0x5C)) && Native.KeyDown(0x10) && Native.KeyDown(0x53);
        if (snip && !snipKeyWasDown && card is not null) captureUntil = clock.Elapsed + TimeSpan.FromSeconds(20);
        snipKeyWasDown = snip;
        if (paused || diagnosing || settings is { IsDisposed: false, Visible: true }) return;
        var point = Cursor.Position;
        var foreground = Native.ForegroundProcess();
        if (card is { IsDisposed: false })
        {
            if (card.Manual || card.Busy || clock.Elapsed < captureUntil) return;
            if (card.Bounds.Contains(point) || (anchor.Contains(point) && foreground == anchorProcess))
            { outsideSince = TimeSpan.Zero; return; }
            if (outsideSince == TimeSpan.Zero) outsideSince = clock.Elapsed;
            if (clock.Elapsed - outsideSince < TimeSpan.FromMilliseconds(650)) return;
            CloseCard(); gate.Reset();
        }
        if (preferences.AltHover && !Native.KeyDown(0x12)) { gate.Observe(null, clock.Elapsed); return; }
        if (foreground == Environment.ProcessId) { gate.Observe(null, clock.Elapsed); return; }
        if (inspection is { IsCompleted: true })
        {
            var result = inspection.IsCompletedSuccessfully ? inspection.Result : null;
            inspection = null;
            if (foreground == inspectedProcess && Math.Abs(point.X - inspectedPoint.X) <= 3 &&
                Math.Abs(point.Y - inspectedPoint.Y) <= 3)
            {
                var key = result?.Link.Text;
                if (suppressed is not null && key != suppressed) suppressed = null;
                gate.Observe(key == suppressed ? null : key, clock.Elapsed);
                if (result is not null && gate.Start(clock.Elapsed, TimeSpan.FromMilliseconds(preferences.DelayMs)))
                { ShowCard(result.Link, result.Bounds, point, false); return; }
            }
            else gate.Reset();
        }
        if (inspection is null)
        {
            inspectedPoint = point; inspectedProcess = foreground;
            inspection = Task.Run(() => LinkInspector.Inspect(point, foreground));
        }
    }

    protected override void ExitThreadCore()
    {
        timer.Stop(); timer.Dispose(); CloseCard(); settings?.Close();
        approvals?.Invalidate(); client?.Dispose(); tray.Visible = false; tray.Dispose(); icon.Dispose();
        base.ExitThreadCore();
    }
}
