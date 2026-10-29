using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using LinkPeek.Core;

namespace LinkPeek.Windows;

// Uses only synthetic data and renders WPF visuals offscreen with RenderTargetBitmap; never
// captures the user's screen, reads credentials, sends API requests, or changes settings or
// startup registration. Each window is laid out once in device-independent pixels (as WPF does
// on every display) and rasterized at 100%, 150% and 200% scale.
internal static class UiPreview
{
    // Long enough for NavigationView pane and page transitions to finish.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(900);
    private static readonly (double Scale, string Name)[] Scales = [(1.0, "100"), (2.0, "200")];

    public static IReadOnlyList<string> Render(string directory)
    {
        Directory.CreateDirectory(directory);
        App.PreviewMode = true;
        _ = App.Instance;
        var files = new List<string>();
        var issues = new List<string>();
        foreach (var dark in new[] { false, true })
        {
            App.ApplyPreviewTheme(dark);
            var theme = dark ? "dark" : "light";
            foreach (var (page, saved, name) in new[]
            {
                (SettingsPage.General, false, "settings-general"), (SettingsPage.QrLinks, false, "settings-qr-links"),
                (SettingsPage.AzureDevOps, false, "settings-azure"),
                (SettingsPage.AzureDevOps, true, "settings-azure-pat-saved"), (SettingsPage.About, false, "settings-about")
            })
            {
                var settings = new SettingsWindow(new Preferences { Organization = "contoso" }, () => { }, preview: true,
                    credentialExists: _ => saved);
                Open(settings); settings.ShowPage(page); Pump(Settle);
                Capture(settings, theme, name);
                if (page != SettingsPage.About)
                {
                    // Minimum window size is the tightest layout users can reach.
                    settings.Width = settings.MinWidth; settings.Height = settings.MinHeight; Pump(Settle);
                    Capture(settings, theme, name + "-minimum");
                }
                settings.Close();
            }

            var web = WebLink.Parse("https://github.com/nqnhut92/LinkPeek?tab=readme#install")!;
            Card(new CardWindow(web, null, null, new Preferences(), true, () => { }, () => { }), "card-qr");
            var prLink = WebLink.Parse("https://dev.azure.com/contoso/Platform/_git/linkpeek/pullrequest/142")!;
            var snapshot = new Snapshot(new PullRequest(142, "Make links feel at home on Windows", "active", false,
                new Commit(new string('a', 40)), new Repository("sample", "linkpeek"), new Person("sample", "Alex Chen"),
                "refs/heads/feature/windows", "refs/heads/main"), "preview", 0);
            Card(new CardWindow(prLink, null, null, new Preferences(), true, () => { }, () => { }, snapshot), "card-pull-request");
            Card(new CardWindow(prLink, null, null, new Preferences { SkipConfirmation = true }, true, () => { }, () => { }, snapshot),
                "card-pull-request-no-confirmation");
            // No client: the card shows the configuration error state without network access.
            Card(new CardWindow(prLink, null, null, new Preferences(), true, () => { }, () => { }), "card-pull-request-not-configured");
            // Hover cards are placed against a link; the arrow faces it from below or above.
            var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            Card(Placed(web, new System.Drawing.Rectangle(work.Left + 220, work.Top + 80, 170, 18)), "card-qr-below-link");
            Card(Placed(web, new System.Drawing.Rectangle(work.Left + 220, work.Bottom - 50, 170, 18)), "card-qr-above-link");
            var besideCard = Placed(web, new System.Drawing.Rectangle(work.Left + 220, work.Top + 250, 170, 18));
            besideCard.WorkAreaOverride = new System.Drawing.Rectangle(work.Left, work.Top, work.Width, 520);
            Card(besideCard, "card-qr-beside-link");

            var open = new OpenLinkWindow(); Open(open); open.PreviewInvalid("ftp://example.org"); Pump();
            Capture(open, theme, "open-link"); open.Close();
            var confirm = DialogWindow.PreviewConfirmation(); Open(confirm); Capture(confirm, theme, "dialog-approve"); confirm.Close();
            Menu(theme);

            CardWindow Placed(WebLink link, System.Drawing.Rectangle anchor)
            {
                var card = new CardWindow(link, null, null, new Preferences(), false, () => { }, () => { });
                card.Place(anchor, new System.Drawing.Point(anchor.Left + 40, anchor.Top + 9));
                return card;
            }

            void Card(CardWindow card, string name)
            {
                Open(card); Pump(); Capture(card, theme, name); card.Close();
            }
        }
        if (issues.Count > 0) throw new InvalidOperationException("UI layout audit failed:\n" + string.Join("\n", issues));
        return files;

        void Capture(Window window, string theme, string name)
        {
            var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            Audit(root, $"{theme}/{name}", issues, window.SizeToContent == SizeToContent.Manual);
            foreach (var (scale, suffix) in Scales)
                files.Add(SaveFramed(root, window.Background, Path.Combine(directory, theme, $"{name}@{suffix}.png"), scale, 8, framed: window is not CardWindow));
        }

        void Menu(string theme)
        {
            var owner = TrayApplication.CreateMenuHost();
            owner.Left = 24; owner.Top = 24; owner.ShowActivated = false; owner.Show();
            var menu = TrayApplication.CreateMenu(() => { }, () => { });
            menu.PlacementTarget = owner; menu.Placement = PlacementMode.Relative; menu.IsOpen = true;
            Pump(TimeSpan.FromSeconds(1)); // Let the Fluent open animation finish.
            // The popup root includes the menu's border and shadow margin.
            DependencyObject popupRoot = menu;
            while (VisualTreeHelper.GetParent(popupRoot) is { } parent) popupRoot = parent;
            var rootElement = (FrameworkElement)popupRoot;
            Audit(rootElement, $"{theme}/tray-menu", issues, false);
            foreach (var (scale, suffix) in Scales)
                files.Add(SaveFramed(rootElement, null, Path.Combine(directory, theme, $"tray-menu@{suffix}.png"), scale, 0));
            menu.IsOpen = false; owner.Close();
        }
    }

    private static void Open(Window window)
    {
        window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 24; window.Top = 24; window.Show(); Pump();
    }

    private static void Pump(TimeSpan? settle = null)
    {
        for (var i = 0; i < 4; i++) App.DoEvents();
        var until = DateTime.UtcNow + (settle ?? TimeSpan.Zero);
        while (DateTime.UtcNow < until) { Thread.Sleep(15); App.DoEvents(); }
    }

    // Approximates the Windows 11 frame (rounded corners, hairline border) on a neutral desktop.
    private static string SaveFramed(FrameworkElement root, Brush? background, string path, double scale, double radius, bool framed = true)
    {
        const double margin = 16;
        var width = root.ActualWidth; var height = root.ActualHeight;
        if (width < 100 || height < 100) throw new InvalidOperationException($"Invalid UI preview dimensions for {path}.");
        var dark = App.Current.TryFindResource("ApplicationBackgroundBrush") is SolidColorBrush { Color.R: < 128 };
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(10, 10, 10) : Color.FromRgb(220, 224, 230)), null,
                new Rect(0, 0, width + margin * 2, height + margin * 2));
            var frame = new Rect(margin, margin, width, height);
            if (framed) context.PushClip(new RectangleGeometry(frame, radius, radius));
            if (background is not null) context.DrawRectangle(background, null, frame);
            context.DrawRectangle(new VisualBrush(root)
            {
                Viewbox = new Rect(0, 0, width, height), ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
            }, null, frame);
            if (framed) context.Pop();
            if (framed) context.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(dark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0)), 1),
                new Rect(margin - 0.5, margin - 0.5, width + 1, height + 1), radius, radius);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling((width + margin * 2) * scale),
            (int)Math.Ceiling((height + margin * 2) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, path);
        return path;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // Fails CI when text is cut off or a control is pushed outside its window.
    private static void Audit(FrameworkElement root, string name, List<string> issues, bool scrollsVertically)
    {
        var dpi = VisualTreeHelper.GetDpi(root).PixelsPerDip;
        Walk(root, insideScroll: false);

        void Walk(DependencyObject node, bool insideScroll)
        {
            if (node is UIElement { IsVisible: false }) return;
            if (node is ScrollViewer) insideScroll = true;
            if (node is FrameworkElement element && element.ActualWidth > 0 && element != root &&
                element is System.Windows.Controls.TextBlock or ButtonBase or TextBox or PasswordBox or Slider or Image)
            {
                var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                var vertical = !(insideScroll || scrollsVertically);
                if (bounds.Left < -1 || bounds.Right > root.ActualWidth + 1 || vertical && (bounds.Top < -1 || bounds.Bottom > root.ActualHeight + 1))
                    issues.Add($"{name}: {Describe(element)} lies outside the window ({bounds}).");
                if (element is System.Windows.Controls.TextBlock { TextWrapping: TextWrapping.NoWrap, TextTrimming: TextTrimming.None } text &&
                    text.Text.Length > 0)
                {
                    var measured = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, dpi).Width;
                    if (measured > text.ActualWidth + 1) issues.Add($"{name}: text \"{text.Text}\" is clipped ({measured:0.#} > {text.ActualWidth:0.#}).");
                }
                // A wrapping label squeezed into a sliver still "fits" but is unreadable.
                if (element is System.Windows.Controls.TextBlock { TextWrapping: not TextWrapping.NoWrap } wrapped &&
                    wrapped.Text.Length > 12 && wrapped.ActualWidth < 120)
                    issues.Add($"{name}: text \"{wrapped.Text}\" is squeezed to {wrapped.ActualWidth:0.#} px.");
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), insideScroll);
        }
    }

    private static string Describe(FrameworkElement element) => element switch
    {
        System.Windows.Controls.TextBlock text => $"text \"{text.Text}\"",
        ContentControl { Content: string content } => $"{element.GetType().Name} \"{content}\"",
        _ => element.GetType().Name + (element.Name.Length > 0 ? " " + element.Name : "")
    };

    // Saved-PAT state: the mask must sit exactly where typed glyphs appear, read as a placeholder,
    // leave the PasswordBox empty, have no automation peer, and disappear as soon as typing starts.
    // The popover must point at the link, stay on screen, never cover the link, and flip above
    // when there is no room below.
    public static string VerifyCardPlacement()
    {
        App.PreviewMode = false;
        _ = App.Instance;
        App.FollowSystemTheme(force: true);
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var link = WebLink.Parse("https://example.org/placement")!;
        var report = new List<string>();
        void Check(string name, System.Drawing.Rectangle anchor, ArrowSide[] expected, System.Drawing.Rectangle? area = null)
        {
            var card = new CardWindow(link, null, null, new Preferences(), false, () => { }, () => { }) { WorkAreaOverride = area };
            card.Place(anchor, new System.Drawing.Point(anchor.Left + Math.Min(40, anchor.Width / 2), anchor.Top + anchor.Height / 2));
            card.ShowActivated = false; card.Show(); Pump(TimeSpan.FromMilliseconds(300));
            var bounds = card.ScreenBounds; var arrow = card.Arrow; var screen = area ?? work;
            card.Close();
            report.Add($"{name}: card {bounds}, anchor {anchor}, arrow {arrow}");
            if (bounds.IntersectsWith(anchor)) throw new InvalidOperationException("Card covers its link. " + report[^1]);
            if (bounds.Left < screen.Left || bounds.Right > screen.Right || bounds.Top < screen.Top || bounds.Bottom > screen.Bottom)
                throw new InvalidOperationException("Card leaves the work area. " + report[^1]);
            var onSide = arrow switch
            {
                ArrowSide.Top => bounds.Top >= anchor.Bottom,
                ArrowSide.Bottom => bounds.Bottom <= anchor.Top,
                ArrowSide.Left => bounds.Left >= anchor.Right,
                ArrowSide.Right => bounds.Right <= anchor.Left,
                _ => false
            };
            if (!expected.Contains(arrow) || !onSide)
                throw new InvalidOperationException("Card or arrow is on the wrong side of the link. " + report[^1]);
        }
        var shortScreen = new System.Drawing.Rectangle(work.Left, work.Top, work.Width, 520);
        Check("below", new System.Drawing.Rectangle(work.Left + 200, work.Top + 60, 160, 18), [ArrowSide.Top]);
        Check("above", new System.Drawing.Rectangle(work.Left + 200, work.Bottom - 50, 160, 18), [ArrowSide.Bottom]);
        Check("right edge", new System.Drawing.Rectangle(work.Right - 40, work.Top + 60, 36, 18), [ArrowSide.Top]);
        Check("left edge", new System.Drawing.Rectangle(work.Left + 2, work.Top + 60, 36, 18), [ArrowSide.Top]);
        // A short screen has room neither above nor below the link: the card goes beside it.
        Check("beside", new System.Drawing.Rectangle(work.Left + 200, work.Top + 250, 160, 18), [ArrowSide.Left, ArrowSide.Right], shortScreen);
        return string.Join("; ", report);
    }

    // Closing Settings once crashed the app: the theme watcher was detached after the window
    // handle was gone. The crash was reported through the dispatcher, so listen for it here.
    public static void VerifySettingsCloseDoesNotThrow()
    {
        App.PreviewMode = false;
        _ = App.Instance;
        Exception? failure = null;
        DispatcherUnhandledExceptionEventHandler handler = (_, e) => { failure = e.Exception; e.Handled = true; };
        Application.Current.DispatcherUnhandledException += handler;
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var settings = new SettingsWindow(new Preferences { Organization = "contoso" }, () => { }, preview: false, credentialExists: _ => false);
                settings.ShowActivated = false; settings.Show(); Pump(TimeSpan.FromMilliseconds(200));
                settings.Close(); Pump(TimeSpan.FromMilliseconds(200));
            }
            // A window closed before it was ever shown must not throw either.
            new SettingsWindow(new Preferences(), () => { }, preview: false, credentialExists: _ => false).Close();
            Pump(TimeSpan.FromMilliseconds(100));
        }
        finally { Application.Current.DispatcherUnhandledException -= handler; }
        if (failure is not null) throw new InvalidOperationException("Closing Settings raised an unhandled exception.", failure);
    }

    // Long-running tray sessions open and close many cards. Each cycle must leave no window,
    // GDI or USER handles behind, and an unchanged theme must not be re-applied per hover.
    public static string VerifyCardLifecycle(int cycles = 200)
    {
        App.PreviewMode = false;
        _ = App.Instance;
        App.FollowSystemTheme(force: true);
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        var themeChanges = 0;
        Wpf.Ui.Appearance.ThemeChangedEvent counter = (_, _) => themeChanges++;
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += counter;
        var web = WebLink.Parse("https://example.org/linkpeek/lifecycle?q=1#x")!;
        var pr = WebLink.Parse("https://dev.azure.com/contoso/Platform/_git/linkpeek/pullrequest/7")!;
        var process = System.Diagnostics.Process.GetCurrentProcess().Handle;
        (uint Gdi, uint User) Handles() => (GetGuiResources(process, 0), GetGuiResources(process, 1));
        void Cycle(int i)
        {
            App.FollowSystemTheme();
            var card = new CardWindow(i % 2 == 0 ? web : pr, null, null, new Preferences(), false, () => { }, () => { });
            card.Place(new System.Drawing.Rectangle(200 + i % 50, 200, 120, 18), new System.Drawing.Point(210 + i % 50, 205));
            card.Show(); App.DoEvents();
            if (card.ScreenBounds.IsEmpty) throw new InvalidOperationException("Card has no screen bounds after Show.");
            card.Close(); App.DoEvents();
        }
        try
        {
            for (var i = 0; i < 20; i++) Cycle(i);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); App.DoEvents();
            var before = Handles();
            for (var i = 0; i < cycles; i++) Cycle(i);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); App.DoEvents();
            var after = Handles();
            var open = Application.Current.Windows.OfType<CardWindow>().Count();
            var report = $"{cycles} card cycles: GDI {before.Gdi}->{after.Gdi}, USER {before.User}->{after.User}, open cards {open}, theme re-applied {themeChanges}x";
            if (open != 0) throw new InvalidOperationException("Closed cards are still registered as windows. " + report);
            if (after.Gdi > before.Gdi + 40 || after.User > before.User + 40) throw new InvalidOperationException("Card lifecycle leaks handles. " + report);
            if (themeChanges != 0) throw new InvalidOperationException("Unchanged Windows theme was re-applied during hover. " + report);
            if (Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() != theme) throw new InvalidOperationException("Theme changed during the lifecycle check.");
            return report;
        }
        finally { Wpf.Ui.Appearance.ApplicationThemeManager.Changed -= counter; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    public static void VerifySavedPatMask(string directory)
    {
        App.PreviewMode = true;
        _ = App.Instance;
        var log = new List<string>();
        try
        {
            foreach (var dark in new[] { false, true })
            {
                App.ApplyPreviewTheme(dark);
                var theme = dark ? "dark" : "light";
                var settings = new SettingsWindow(new Preferences { Organization = "contoso" }, () => { }, preview: true,
                    credentialExists: _ => true);
                Open(settings); settings.ShowPage(SettingsPage.AzureDevOps); Pump();
                var page = settings.AzurePage;
                var box = page.Token; var mask = page.SavedMask;
                if (box.SecurePassword.Length != 0) throw new InvalidOperationException("Saved PAT state must leave the input empty.");
                if (!mask.IsShowing) throw new InvalidOperationException("Saved PAT mask is not shown for an existing credential.");
                if (UIElementAutomationPeer.CreatePeerForElement(mask) is not null)
                    throw new InvalidOperationException("Saved PAT mask must not appear in the automation tree.");
                var field = (FrameworkElement)box.Parent;
                var host = (FrameworkElement)box.Template.FindName("PART_ContentHost", box);
                var region = host.TransformToAncestor(field).TransformBounds(new Rect(host.RenderSize));
                const double scale = 2;
                var saved = Rasterize(field, scale, Path.Combine(directory, theme, "pat-saved-mask@200.png"));

                box.Password = "x"; Pump();
                if (mask.IsShowing) throw new InvalidOperationException("Saved PAT mask must hide while typing.");
                mask.IsSaved = false; box.Password = new string('0', SavedSecretMask.GlyphCount); Pump();
                var typed = Rasterize(field, scale, Path.Combine(directory, theme, "pat-typed-reference@200.png"));
                box.Clear(); settings.Close();

                var savedInk = Ink(saved, region, scale); var typedInk = Ink(typed, region, scale);
                log.Add($"{theme}: mask bounds {savedInk.Bounds}, contrast {savedInk.Contrast:0.000}; typed bounds {typedInk.Bounds}, contrast {typedInk.Contrast:0.000}");
                if (savedInk.Bounds.IsEmpty || typedInk.Bounds.IsEmpty) throw new InvalidOperationException($"{theme}: PAT glyphs were not rendered.");
                // Antialiasing with a lighter ink can move an edge by one physical pixel per side at 200%.
                var tolerance = 2 * scale;
                if (Math.Abs(savedInk.Bounds.Left - typedInk.Bounds.Left) > tolerance || Math.Abs(savedInk.Bounds.Right - typedInk.Bounds.Right) > tolerance ||
                    Math.Abs(savedInk.Bounds.Top - typedInk.Bounds.Top) > tolerance || Math.Abs(savedInk.Bounds.Bottom - typedInk.Bounds.Bottom) > tolerance)
                    throw new InvalidOperationException($"{theme}: saved PAT mask is misaligned with typed glyphs. {log[^1]}");
                if (savedInk.Contrast > typedInk.Contrast - 0.15 || savedInk.Contrast < 0.15)
                    throw new InvalidOperationException($"{theme}: saved PAT mask must use a visible placeholder tone lighter than typed text. {log[^1]}");
            }
        }
        finally { File.WriteAllLines(Path.Combine(directory, "pat-mask-check.txt"), log); }
    }

    private static BitmapSource Rasterize(FrameworkElement element, double scale, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
            context.DrawRectangle(App.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? Brushes.White, null,
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(element);
        Save(bitmap, path);
        return bitmap;
    }

    // Glyph pixels differ from the field background; contrast is their strongest luminance difference.
    private static (Rect Bounds, double Contrast) Ink(BitmapSource bitmap, Rect region, double scale)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var left = (int)Math.Ceiling(region.Left * scale) + 2; var right = (int)Math.Floor(region.Right * scale) - 2;
        var top = (int)Math.Ceiling(region.Top * scale) + 2; var bottom = (int)Math.Floor(region.Bottom * scale) - 2;
        double Luma(int x, int y) { var i = y * stride + x * 4; return (0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2]) / 255; }
        var samples = new List<double>();
        for (var y = top; y < bottom; y++) for (var x = left; x < right; x++) samples.Add(Luma(x, y));
        samples.Sort();
        var background = samples[samples.Count / 2];
        var contrast = samples.Max(v => Math.Abs(v - background));
        var threshold = contrast / 2;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
                if (Math.Abs(Luma(x, y) - background) > threshold)
                { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        return (maxX < 0 ? Rect.Empty : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), contrast);
    }
}
