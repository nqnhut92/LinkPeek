using System.Diagnostics;
using System.Drawing;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using WinForms = System.Windows.Forms;

namespace LinkPeek.Windows;

internal sealed class TrayApplication : IDisposable
{
    private readonly App app;
    private readonly WinForms.NotifyIcon tray;
    private readonly Icon icon;
    private readonly System.Windows.Controls.ContextMenu menu;
    private readonly System.Windows.Window menuHost;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Preferences preferences = Preferences.Load();
    private readonly DwellGate gate = new();
    private CardWindow? card;
    private SettingsWindow? settings;
    private Task<Inspection?>? inspection;
    private Point inspectedPoint;
    private uint inspectedProcess;
    private Rectangle anchor;
    private uint anchorProcess;
    private string? suppressed;
    private bool paused;
    private TimeSpan outsideSince;
    private TimeSpan captureUntil;
    private bool snipKeyWasDown;
    private AzureDevOpsClient? client;
    private ApprovalService? approvals;

    public TrayApplication(App app)
    {
        this.app = app;
        App.InstallCrashGuards(NotifyRecovered);
        using (var stream = System.Windows.Application.GetResourceStream(App.Asset("LinkPeek.ico")).Stream)
            icon = new Icon(stream, WinForms.SystemInformation.SmallIconSize);
        menu = CreateMenu(OpenLink, () => OpenSettings(),
            value => { paused = value; CloseCard(); gate.Reset(); }, () => OpenSettings(SettingsPage.About), Quit);
        // A WPF popup only dismisses on outside clicks when one of our windows is foreground,
        // so the tray menu opens from an invisible, off-screen owner window.
        menuHost = CreateMenuHost();
        menu.PlacementTarget = menuHost;
        menu.Closed += (_, _) => menuHost.Hide();
        tray = new WinForms.NotifyIcon { Text = "LinkPeek", Icon = icon, Visible = true };
        tray.MouseUp += (_, e) => { if (e.Button == WinForms.MouseButtons.Right) ShowMenu(); };
        tray.DoubleClick += (_, _) => OpenSettings();
        App.FollowSystemTheme(force: true);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ReloadClient();
        timer.Tick += (_, _) => Tick(); timer.Start();
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color or
            Microsoft.Win32.UserPreferenceCategory.VisualStyle or Microsoft.Win32.UserPreferenceCategory.Accessibility)
            app.Dispatcher.BeginInvoke(() => App.FollowSystemTheme(force: true));
    }

    private bool recoveryShown;
    private void NotifyRecovered()
    {
        if (tray is null) return; // Still constructing.
        CloseCard(); gate.Reset();
        if (recoveryShown || !tray.Visible) return;
        recoveryShown = true;
        tray.ShowBalloonTip(5000, "LinkPeek", "LinkPeek recovered from an unexpected error and is still running. Details: " +
            CrashLog.FilePath, WinForms.ToolTipIcon.Warning);
    }

    // An empty style keeps WPF UI's window style (which sets AllowsTransparency) off this helper.
    internal static System.Windows.Window CreateMenuHost() => new()
    {
        Style = new System.Windows.Style(typeof(System.Windows.Window)), WindowStyle = System.Windows.WindowStyle.None,
        ShowInTaskbar = false, ShowActivated = true, Width = 1, Height = 1, Left = -32000, Top = -32000,
        ResizeMode = System.Windows.ResizeMode.NoResize
    };

    internal static System.Windows.Controls.ContextMenu CreateMenu(Action openLink, Action openSettings,
        Action<bool> pauseChanged, Action about, Action quit)
    {
        var result = new System.Windows.Controls.ContextMenu { Placement = PlacementMode.MousePoint };
        result.Items.Add(Item("Open link…", SymbolRegular.Open24, openLink));
        result.Items.Add(Item("Settings", SymbolRegular.Settings24, openSettings));
        var pause = new MenuItem { Header = "Pause hover", IsCheckable = true };
        pause.Click += (_, _) => pauseChanged(pause.IsChecked);
        result.Items.Add(pause);
        result.Items.Add(new System.Windows.Controls.Separator());
        result.Items.Add(Item("About LinkPeek", SymbolRegular.Info24, about));
        result.Items.Add(Item("Quit", SymbolRegular.Power24, quit));
        return result;
    }

    private static MenuItem Item(string text, SymbolRegular symbol, Action action)
    {
        var item = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = symbol } };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowMenu()
    {
        CloseCard(); gate.Reset(); App.FollowSystemTheme();
        menuHost.Show(); menuHost.Activate();
        menu.IsOpen = true;
    }

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

    private void OpenSettings(SettingsPage page = SettingsPage.General)
    {
        CloseCard(); gate.Reset(); App.FollowSystemTheme();
        if (settings is { IsLoaded: true })
        {
            settings.ShowPage(page);
            if (settings.WindowState == System.Windows.WindowState.Minimized) settings.WindowState = System.Windows.WindowState.Normal;
            settings.Activate(); return;
        }
        var opened = new SettingsWindow(preferences, ReloadClient);
        settings = opened;
        opened.Closed += (_, _) => { if (settings == opened) settings = null; };
        opened.Show(); opened.ShowPage(page); opened.Activate();
    }

    private void OpenLink()
    {
        CloseCard(); gate.Reset(); App.FollowSystemTheme();
        var dialog = new OpenLinkWindow();
        if (dialog.ShowDialog() == true && dialog.Link is { } link)
            ShowCard(link, Rectangle.Empty, WinForms.Cursor.Position, true);
    }

    private void CloseCard()
    {
        var old = card; card = null; old?.Close();
    }

    private void ShowCard(WebLink link, Rectangle bounds, Point point, bool manual)
    {
        CloseCard(); anchor = bounds; anchorProcess = Native.ForegroundProcess(); outsideSince = TimeSpan.Zero;
        captureUntil = TimeSpan.Zero; App.FollowSystemTheme();
        var current = new CardWindow(link, client, approvals, preferences with { }, manual,
            () => suppressed = link.Text,
            () => tray.ShowBalloonTip(3000, "LinkPeek", "Azure DevOps verified your approval.", WinForms.ToolTipIcon.Info));
        card = current;
        current.Closed += (_, _) => { if (card == current) { card = null; gate.Reset(); } };
        current.Place(bounds, point); current.Show();
    }

    private void Tick()
    {
        if (Native.KeyDown(0x1B) && card is not null && !card.Busy) { CloseCard(); gate.Reset(); }
        // Hold a card while Win+Shift+S is being used. No screenshot pixels are read.
        var snip = (Native.KeyDown(0x5B) || Native.KeyDown(0x5C)) && Native.KeyDown(0x10) && Native.KeyDown(0x53);
        if (snip && !snipKeyWasDown && card is not null) captureUntil = clock.Elapsed + TimeSpan.FromSeconds(20);
        snipKeyWasDown = snip;
        if (paused || menu.IsOpen || settings is { IsVisible: true }) return;
        var point = WinForms.Cursor.Position;
        var foreground = Native.ForegroundProcess();
        if (card is { IsClosed: false })
        {
            if (card.Manual || card.Busy || clock.Elapsed < captureUntil) return;
            if (card.ScreenBounds.Contains(point) || (anchor.Contains(point) && foreground == anchorProcess))
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
                // Pull requests follow their own switch; every other link must pass the QR filter.
                var allowed = result is not null &&
                    (preferences.PrCardEnabled && result.Link.PullRequest is not null || preferences.LinkFilter.AllowsQr(result.Link.Uri));
                var key = allowed ? result!.Link.Text : null;
                if (suppressed is not null && key != suppressed) suppressed = null;
                gate.Observe(key == suppressed ? null : key, clock.Elapsed);
                if (allowed && gate.Start(clock.Elapsed, TimeSpan.FromMilliseconds(preferences.DelayMs)))
                { ShowCard(result!.Link, result.Bounds, point, false); return; }
            }
            else gate.Reset();
        }
        if (inspection is null)
        {
            inspectedPoint = point; inspectedProcess = foreground;
            inspection = Task.Run(() => LinkInspector.Inspect(point, foreground));
        }
    }

    private void Quit()
    {
        Dispose();
        app.Shutdown();
    }

    public void Dispose()
    {
        if (!timer.IsEnabled && !tray.Visible) return;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        timer.Stop(); CloseCard(); settings?.Close(); menuHost.Close();
        approvals?.Invalidate(); client?.Dispose(); tray.Visible = false; tray.Dispose(); icon.Dispose();
    }
}
