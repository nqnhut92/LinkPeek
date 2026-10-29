using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using Wpf.Ui.Controls;
using Drawing = System.Drawing;

namespace LinkPeek.Windows;

internal sealed partial class CardWindow : Window
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
    private PrDisplayState state = PrDisplayState.Loading;
    private string? error;
    private Approval? confirmation;
    private Drawing.Rectangle anchor;
    private Drawing.Point pointer;
    private bool placed;
    private bool busy;
    public bool Busy => busy;
    /// <summary>True while an approval is running or waiting for its confirmation click; the card must stay.</summary>
    public bool Pinned => busy || confirmation is not null;
    /// <summary>Keeps the card up for a moment after a successful approval, like the macOS card.</summary>
    public DateTime ResultUntil { get; private set; } = DateTime.MinValue;
    public Action? OpenSettings { get; set; }
    public bool IsClosed { get; private set; }

    // With PR cards switched off, pull request links get the same QR card as any other link.
    private PullRequestLink? Pr => preferences.PrCardEnabled ? link.PullRequest : null;

    public CardWindow(WebLink link, AzureDevOpsClient? client, ApprovalService? approvals,
        Preferences preferences, Action copied, Action notify, Snapshot? previewSnapshot = null)
    {
        this.link = link; this.client = client; this.approvals = approvals;
        this.preferences = preferences; this.copied = copied; this.notify = notify;
        InitializeComponent();
        ShowActivated = false; // Hover cards close by moving away or Esc.
        if (Pr is null) BuildQr();
        else
        {
            if (previewSnapshot is not null) { snapshot = previewSnapshot; state = PrDisplay.Resolve(previewSnapshot, null); }
            RenderPr();
        }
        // Root is laid out before the HWND is resized; place from its layout size, and once more after render.
        Root.SizeChanged += (_, _) => Reposition();
        ContentRendered += (_, _) => Reposition();
        ContentRendered += async (_, _) => { if (Pr is not null && previewSnapshot is null) await LoadAsync(); };
        Closed += (_, _) => { IsClosed = true; lifetime.Cancel(); };
    }

    private void BuildQr()
    {
        Caption.Text = "LINK QR";
        Heading.Text = link.Uri.Host;
        Detail(null, link.Text.Length > 120 ? link.Text[..120] + "…" : link.Text, caption: true);
        var buttons = new List<ActionSpec> { new("Copy Link", SymbolRegular.Copy24, () => Copy(false), true) };
        try
        {
            using var data = QRCodeGenerator.GenerateQrCode(link.Text, QRCodeGenerator.ECCLevel.M);
            Qr.Source = QrDrawing(data);
            using var png = new PngByteQRCode(data);
            using var stream = new MemoryStream(png.GetGraphic(8));
            qrImage = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            qrImage.Freeze();
            QrFrame.Visibility = Visibility.Visible;
            buttons.Add(new("Copy QR", SymbolRegular.QrCode24, () => Copy(true), false));
        }
        catch (Exception e) when (e is QRCoder.Exceptions.DataTooLongException or ArgumentException)
        { Warn(InfoBarSeverity.Warning, "This link is too long to encode."); }
        SetActions(equal: true, buttons.ToArray());
    }

    // QR modules drawn as vector rectangles stay sharp at every display scale.
    private static DrawingImage QrDrawing(QRCodeData data)
    {
        var modules = data.ModuleMatrix.Count;
        var size = Math.Max(2, 168 / modules);
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

    private void Detail(SymbolRegular? symbol, string text, bool caption = false)
    {
        var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        var label = new Wpf.Ui.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            FontSize = caption ? 12 : 13 };
        label.SetResourceReference(ForegroundProperty, caption ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        if (symbol is { } glyph)
        {
            var icon = new SymbolIcon { Symbol = glyph, FontSize = 14, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            icon.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
            row.Children.Add(icon);
            System.Windows.Controls.Grid.SetColumn(label, 1);
        }
        else System.Windows.Controls.Grid.SetColumnSpan(label, 2);
        row.Children.Add(label); Details.Children.Add(row);
    }

    private void Warn(InfoBarSeverity severity, string message)
    {
        Notice.Severity = severity; Notice.Message = message; Notice.IsOpen = message.Length > 0;
    }

    private sealed record ActionSpec(string Text, SymbolRegular? Symbol, Action? Click, bool Primary, bool Enabled = true);

    // One full-width action like the macOS card; the confirmation step adds a Cancel button beside it.
    private void SetActions(bool equal, params ActionSpec[] specs)
    {
        Actions.Children.Clear(); Actions.ColumnDefinitions.Clear();
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            Actions.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = equal || i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
                MinWidth = i == 0 ? 0 : 84
            });
            var button = new Wpf.Ui.Controls.Button
            {
                Content = spec.Text, IsEnabled = spec.Enabled,
                Appearance = spec.Primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0)
            };
            if (spec.Symbol is { } symbol) button.Icon = new SymbolIcon { Symbol = symbol };
            if (spec.Click is { } click) button.Click += (_, _) => click();
            System.Windows.Controls.Grid.SetColumn(button, i);
            Actions.Children.Add(button);
        }
    }

    private void Copy(bool image)
    {
        try
        {
            if (image && qrImage is not null) Clipboard.SetImage(qrImage);
            else Clipboard.SetText(link.Text);
            copied(); Close();
        }
        catch (ExternalException) { Warn(InfoBarSeverity.Warning, image ? "Could not copy image." : "Could not copy link."); }
    }

    // Same layout and wording as the macOS pull request card: caption, title, creator, repository,
    // branches, one status line, an optional hint and a single action.
    private void RenderPr()
    {
        var pr = (confirmation?.Snapshot ?? snapshot)?.Pr;
        var id = Pr!.Id;
        Caption.Text = $"PULL REQUEST · #{id}";
        Heading.Text = pr?.Title ?? (state == PrDisplayState.Loading ? "Loading pull request…" : Pr.Repository);
        Details.Children.Clear();
        Detail(SymbolRegular.Person24, pr?.CreatedBy?.DisplayName ?? "Creator unavailable");
        Detail(SymbolRegular.Folder24, pr?.Repository.Name ?? Pr.Repository);
        Detail(SymbolRegular.BranchCompare24, pr?.SourceRefName is { } source && pr.TargetRefName is { } target
            ? $"{source.Replace("refs/heads/", "")} → {target.Replace("refs/heads/", "")}" : Pr.Project);
        var shown = confirmation is not null ? "Approve this pull request?"
            : state == PrDisplayState.Loading ? state.Label() : snapshot?.Caption ?? state.Label();
        StatusText.Text = error ?? shown;
        StatusText.FontWeight = confirmation is null ? FontWeights.Medium : FontWeights.SemiBold;
        StatusText.SetResourceReference(ForegroundProperty, error is not null ? "SystemFillColorCriticalBrush"
            : confirmation is not null ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        StatusText.Visibility = Visibility.Visible;
        HintText.Visibility = preferences.SkipConfirmation && confirmation is null && state.CanApprove() && !busy
            ? Visibility.Visible : Visibility.Collapsed;

        Action? click = confirmation is not null ? () => _ = ConfirmApprovalAsync()
            : state.CanApprove() ? () => _ = ApproveAsync()
            : state == PrDisplayState.Configure ? () => OpenSettings?.Invoke()
            : state == PrDisplayState.Error ? () => _ = LoadAsync() : null;
        var label = confirmation is not null ? "Confirm Approve" : state.CanApprove() ? "Approve"
            : state == PrDisplayState.Error ? "Retry" : state.Label();
        var symbol = state.CanApprove() || confirmation is not null ? SymbolRegular.Checkmark24
            : state == PrDisplayState.Error ? SymbolRegular.ArrowClockwise24 : SymbolRegular.Info24;
        var buttons = new List<ActionSpec> { new(label, symbol, click, true, click is not null && !busy) };
        if (confirmation is not null) buttons.Add(new("Cancel", null, CancelConfirmation, false, !busy));
        SetActions(equal: false, buttons.ToArray());
    }

    private async Task LoadAsync()
    {
        var pr = Pr;
        if (IsClosed || pr is null) return;
        snapshot = null; error = null; state = PrDisplayState.Loading; RenderPr();
        try
        {
            if (preferences.Organization.Length == 0) throw new ConfigureException("Configure PAT");
            if (!string.Equals(pr.Organization, preferences.Organization, StringComparison.OrdinalIgnoreCase))
                throw new ConfigureException("Configure this organization");
            if (client is null || approvals is null) throw new ConfigureException("Configure PAT");
            var value = await client.SnapshotAsync(pr, lifetime.Token);
            if (IsClosed) return;
            snapshot = value; state = PrDisplay.Resolve(value, approvals.ReviewedHead(pr, value));
        }
        catch (OperationCanceledException) { return; }
        catch (ConfigureException e) { error = e.Message; state = PrDisplayState.Configure; }
        catch (Exception e) { error = SafeMessage(e); state = PrDisplayState.Error; }
        if (!IsClosed) RenderPr();
    }

    private sealed class ConfigureException(string message) : Exception(message);

    private async Task ApproveAsync()
    {
        var pr = Pr; var current = snapshot;
        if (busy || pr is null || current is null || !current.Eligible || approvals is null) return;
        busy = true; state = PrDisplayState.Loading; error = null; RenderPr();
        try
        {
            var prepared = await approvals.PrepareAsync(pr, current, lifetime.Token);
            if (IsClosed) return;
            if (preferences.SkipConfirmation)
            {
                snapshot = await approvals.ApproveAsync(prepared, lifetime.Token);
                Succeeded();
            }
            else { confirmation = prepared; snapshot = prepared.Snapshot; state = PrDisplayState.Approve; }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { state = PrDisplayState.Error; error = SafeMessage(e); }
        finally { busy = false; }
        if (!IsClosed) RenderPr();
    }

    private async Task ConfirmApprovalAsync()
    {
        var prepared = confirmation;
        if (busy || prepared is null || approvals is null) return;
        confirmation = null; busy = true; state = PrDisplayState.Loading; error = null; RenderPr();
        try { snapshot = await approvals.ApproveAsync(prepared, lifetime.Token); Succeeded(); }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { snapshot = null; state = PrDisplayState.Error; error = SafeMessage(e); }
        finally { busy = false; }
        if (!IsClosed) RenderPr();
    }

    private void CancelConfirmation()
    {
        confirmation = null;
        if (snapshot is not null) state = PrDisplay.Resolve(snapshot, null);
        RenderPr();
    }

    private void Succeeded()
    {
        state = PrDisplayState.Approved;
        ResultUntil = DateTime.UtcNow.AddSeconds(3);
        if (preferences.SuccessNotification) notify();
    }

    internal static string SafeMessage(Exception error) => error is UserError
        ? error.Message : "Operation failed. Check Settings and try again.";

    // Screen geometry is in physical pixels, matching Cursor.Position and UI Automation bounds.
    // Bounds describe the visible card and arrow, excluding the transparent shadow margin.
    public Drawing.Rectangle ScreenBounds
    {
        get
        {
            if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var r)) return default;
            var scale = Scale; var inset = (int)Math.Round(ShadowMargin * scale);
            // Size comes from layout: a SizeToContent window's HWND can briefly report its old size.
            return new Drawing.Rectangle(r.Left + inset, r.Top + inset,
                (int)Math.Round(Root.ActualWidth * scale), (int)Math.Round(Root.ActualHeight * scale));
        }
    }

    internal ArrowSide Arrow { get; private set; }

    // Lets desktop checks simulate a short screen; normal use reads the pointer's monitor.
    internal Drawing.Rectangle? WorkAreaOverride { get; set; }

    private const double ShadowMargin = 16, Band = 9, ArrowInset = 22, ScreenGap = 6, TipGap = 4, LineHeight = 28;
    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public void Place(Drawing.Rectangle anchor, Drawing.Point point)
    {
        this.anchor = anchor;
        pointer = point; placed = true;
        // Create the window on the pointer's monitor so WPF picks that monitor's DPI before layout.
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, point.X, this.anchor.Bottom, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    // The arrow tip touches the link. PopoverPlacement picks below, above or beside the link (or
    // the pointer) so that the card stays on screen without detaching from it.
    private void Reposition()
    {
        if (!placed) return;
        var current = ScreenBounds;
        if (current.IsEmpty) return;
        var scale = Scale;
        var work = WorkAreaOverride ?? System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
        var layout = PopoverPlacement.Compute(anchor, pointer, work, current.Size,
            gap: (int)Math.Round(ScreenGap * scale), band: (int)Math.Round(Band * scale), inset: (int)Math.Round(ArrowInset * scale),
            tipGap: (int)Math.Round(TipGap * scale), lineHeight: (int)Math.Round(LineHeight * scale));
        Arrow = layout.Arrow;
        ArrowTop.Visibility = layout.Arrow == ArrowSide.Top ? Visibility.Visible : Visibility.Hidden;
        ArrowBottom.Visibility = layout.Arrow == ArrowSide.Bottom ? Visibility.Visible : Visibility.Hidden;
        ArrowLeft.Visibility = layout.Arrow == ArrowSide.Left ? Visibility.Visible : Visibility.Hidden;
        ArrowRight.Visibility = layout.Arrow == ArrowSide.Right ? Visibility.Visible : Visibility.Hidden;
        // Offsets are measured from the popover edge; arrows are laid out inside the 9 px bands.
        var along = layout.ArrowOffset / scale;
        ArrowTop.Margin = new Thickness(along - Band - ArrowTop.Width / 2, 0, 0, -1);
        ArrowBottom.Margin = new Thickness(along - Band - ArrowBottom.Width / 2, -1, 0, 0);
        ArrowLeft.Margin = new Thickness(0, along - Band - ArrowLeft.Height / 2, -1, 0);
        ArrowRight.Margin = new Thickness(-1, along - Band - ArrowRight.Height / 2, 0, 0);
        var inset = ShadowMargin * scale;
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(layout.Location.X - inset), (int)Math.Round(layout.Location.Y - inset), 0, 0, NoSize | NoZOrder | NoActivate);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64() | 0x00000080; // WS_EX_TOOLWINDOW
        style |= 0x08000000; // WS_EX_NOACTIVATE: hover cards never take focus.
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
