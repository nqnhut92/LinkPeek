using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using Wpf.Ui.Controls;
using Drawing = System.Drawing;

namespace LinkPeek.Windows;

internal sealed partial class CardWindow : FluentWindow
{
    private readonly WebLink link;
    private readonly AzureDevOpsClient? client;
    private readonly ApprovalService? approvals;
    private readonly Preferences preferences;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Action copied;
    private readonly Action notify;
    private Snapshot? snapshot;
    private BitmapSource? qrImage;
    private Wpf.Ui.Controls.Button? approve;
    private Drawing.Rectangle anchor;
    private Drawing.Point pointer;
    private bool placed;
    private bool busy;
    public bool Busy => busy;
    public bool Manual { get; }
    public bool IsClosed { get; private set; }

    public CardWindow(WebLink link, AzureDevOpsClient? client, ApprovalService? approvals,
        Preferences preferences, bool manual, Action copied, Action notify, Snapshot? previewSnapshot = null)
    {
        this.link = link; this.client = client; this.approvals = approvals;
        this.preferences = preferences; this.copied = copied; this.notify = notify; Manual = manual;
        InitializeComponent();
        ShowActivated = manual;
        Logo.Source = App.Logo;
        CloseButton.Click += (_, _) => Close();
        Heading.Text = link.PullRequest is { } pr ? $"Pull request #{pr.Id}" : "Share this link";
        Detail(SymbolRegular.Globe24, link.Uri.Host);
        Detail(SymbolRegular.Link24, link.Text.Length > 120 ? link.Text[..120] + "…" : link.Text, caption: true);
        if (link.PullRequest is null)
        {
            AddAction("Copy link", SymbolRegular.Copy24, () => Copy(false), true);
            try
            {
                using var data = QRCodeGenerator.GenerateQrCode(link.Text, QRCodeGenerator.ECCLevel.M);
                Qr.Source = QrDrawing(data);
                using var png = new PngByteQRCode(data);
                using var stream = new MemoryStream(png.GetGraphic(8));
                qrImage = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                qrImage.Freeze();
                QrFrame.Visibility = Visibility.Visible;
                Status("Scan to open · Generated on your device");
                AddAction("Copy QR", SymbolRegular.QrCode24, () => Copy(true));
            }
            catch (Exception e) when (e is QRCoder.Exceptions.DataTooLongException or ArgumentException)
            { Warn(InfoBarSeverity.Warning, "This URL is too large for a QR code. Copy link is still available."); }
        }
        else
        {
            approve = AddAction(preferences.SkipConfirmation ? "Approve immediately" : "Approve…",
                SymbolRegular.Checkmark24, async () => await ApproveAsync(), true);
            approve.IsEnabled = false;
            AddAction("Refresh", SymbolRegular.ArrowClockwise24, async () => await RefreshAsync());
            AddAction("Copy link", SymbolRegular.Copy24, () => Copy(false));
            if (preferences.SkipConfirmation) Warn(InfoBarSeverity.Warning, "Confirmation is off: Approve sends your vote after verification.");
        }
        if (previewSnapshot is not null) Render(previewSnapshot);
        SizeChanged += (_, _) => Reposition();
        ContentRendered += async (_, _) => { if (link.PullRequest is not null && previewSnapshot is null) await RefreshAsync(); };
        Closed += (_, _) => { IsClosed = true; lifetime.Cancel(); };
    }

    // QR modules drawn as vector rectangles stay sharp at every display scale.
    private static DrawingImage QrDrawing(QRCodeData data)
    {
        var modules = data.ModuleMatrix.Count;
        var size = Math.Max(2, 216 / modules);
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var y = 0; y < modules; y++)
            for (var x = 0; x < modules; x++)
                if (data.ModuleMatrix[y][x]) geometry.Children.Add(new RectangleGeometry(new Rect(x * size, y * size, size, size)));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, modules * size, modules * size))));
        group.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
        var image = new DrawingImage(group); image.Freeze();
        return image;
    }

    private void Detail(SymbolRegular symbol, string text, bool caption = false)
    {
        var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 14, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        var label = new Wpf.Ui.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            FontTypography = caption ? FontTypography.Caption : FontTypography.Body };
        label.SetResourceReference(ForegroundProperty, caption ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        System.Windows.Controls.Grid.SetColumn(label, 1);
        row.Children.Add(icon); row.Children.Add(label); Details.Children.Add(row);
    }

    private void Status(string text)
    {
        StatusText.Text = text; StatusText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Warn(InfoBarSeverity severity, string message)
    {
        Notice.Severity = severity; Notice.Message = message; Notice.IsOpen = message.Length > 0;
    }

    private Wpf.Ui.Controls.Button AddAction(string text, SymbolRegular symbol, Action action, bool primary = false)
    {
        var button = new Wpf.Ui.Controls.Button { Content = text, Icon = new SymbolIcon { Symbol = symbol },
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary, Margin = new Thickness(0, 4, 8, 0) };
        button.Click += (_, _) => action();
        Actions.Children.Add(button); return button;
    }

    private void Copy(bool image)
    {
        try
        {
            if (image && qrImage is not null) Clipboard.SetImage(qrImage);
            else Clipboard.SetText(link.Text);
            copied(); Close();
        }
        catch (ExternalException) { Status("Clipboard is busy. Try again."); }
    }

    private void Render(Snapshot value)
    {
        snapshot = value;
        Heading.Text = $"#{value.Pr.PullRequestId} · {value.Pr.Title}";
        Details.Children.Clear();
        Detail(SymbolRegular.Person24, value.Pr.CreatedBy?.DisplayName ?? "Unknown author");
        Detail(SymbolRegular.BranchCompare24,
            $"{value.Pr.SourceRefName?.Replace("refs/heads/", "")} → {value.Pr.TargetRefName?.Replace("refs/heads/", "")}");
        var previous = approvals?.ReviewedHead(link.PullRequest!, value);
        Status(value.Caption);
        var warnings = new List<string>();
        if (previous is not null && previous != value.Head) warnings.Add("New commits · Review changes before approving.");
        if (preferences.SkipConfirmation && value.Eligible) warnings.Add("Confirmation is off: clicking Approve submits a vote.");
        Warn(InfoBarSeverity.Warning, string.Join("\n", warnings));
        if (approve is not null) approve.IsEnabled = value.Eligible && !busy;
    }

    private async Task RefreshAsync()
    {
        if (busy || IsClosed) return;
        busy = true; if (approve is not null) approve.IsEnabled = false;
        try
        {
            if (client is null || approvals is null) throw new UserError("Configure this organization and save a PAT in Settings.");
            Status("Loading…");
            var value = await client.SnapshotAsync(link.PullRequest!, lifetime.Token);
            if (!IsClosed) Render(value);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!IsClosed) { snapshot = null; Status(""); Warn(InfoBarSeverity.Error, SafeMessage(error)); } }
        finally
        {
            busy = false;
            if (!IsClosed && approve is not null) approve.IsEnabled = snapshot?.Eligible == true;
        }
    }

    private async Task ApproveAsync()
    {
        if (busy || snapshot is null || approvals is null) return;
        busy = true; approve!.IsEnabled = false;
        try
        {
            Status("Checking current PR and commit…");
            var prepared = await approvals.PrepareAsync(link.PullRequest!, snapshot, lifetime.Token);
            if (IsClosed) return;
            if (!preferences.SkipConfirmation && !DialogWindow.Confirm(this, $"Approve PR #{snapshot.Pr.PullRequestId}?",
                    $"{snapshot.Pr.Title}\n\nCommit {snapshot.Head}\n\n" +
                    "LinkPeek will recheck this commit and your identity before submitting one vote.",
                    "Approve", "Cancel"))
            { Status("Approval cancelled."); return; }
            Status("Approving and verifying…");
            var verified = await approvals.ApproveAsync(prepared, lifetime.Token);
            if (!IsClosed) { Render(verified); if (preferences.SuccessNotification) notify(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsClosed) { snapshot = null; Status(""); Warn(InfoBarSeverity.Error, SafeMessage(error) + " Use Refresh before trying again."); }
        }
        finally
        {
            busy = false;
            if (!IsClosed) approve.IsEnabled = snapshot?.Eligible == true;
        }
    }

    internal static string SafeMessage(Exception error) => error is UserError
        ? error.Message : "Operation failed. Check Settings and try again.";

    // Screen geometry is in physical pixels, matching Cursor.Position and UI Automation bounds.
    public Drawing.Rectangle ScreenBounds =>
        GetWindowRect(new WindowInteropHelper(this).Handle, out var r) ? Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : default;

    public void Place(Drawing.Rectangle anchor, Drawing.Point point)
    {
        this.anchor = anchor.IsEmpty ? new Drawing.Rectangle(point, new Drawing.Size(1, 12)) : anchor;
        pointer = point; placed = true;
        // Create the window on the pointer's monitor so WPF picks that monitor's DPI before layout.
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, point.X, this.anchor.Bottom + 6, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    private void Reposition()
    {
        if (!placed) return;
        var size = ScreenBounds.Size;
        if (size.IsEmpty) return;
        var work = System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
        var x = Math.Clamp(pointer.X, work.Left, Math.Max(work.Left, work.Right - size.Width));
        var y = anchor.Bottom + 6;
        if (y + size.Height > work.Bottom) y = anchor.Top - size.Height - 6;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - size.Height));
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64() | 0x00000080; // WS_EX_TOOLWINDOW
        if (!Manual) style |= 0x08000000; // WS_EX_NOACTIVATE: hover cards never take focus.
        SetWindowLongPtr(handle, -20, new IntPtr(style));
    }

    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect32 rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
