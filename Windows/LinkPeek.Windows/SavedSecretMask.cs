using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace LinkPeek.Windows;

// Windows counterpart of the macOS PATSecureField: when a PAT exists for the organization and
// the input is empty, draw a fixed row of password glyphs in the placeholder color where typed
// glyphs would appear. The saved token is never read, its length is never reflected, and the
// glyphs are never written into the PasswordBox. Drawing has no automation peer.
internal sealed class SavedSecretMask : FrameworkElement
{
    public const int GlyphCount = 8;

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target),
        typeof(PasswordBox), typeof(SavedSecretMask), new PropertyMetadata(null, OnTargetChanged));
    public static readonly DependencyProperty IsSavedProperty = DependencyProperty.Register(nameof(IsSaved),
        typeof(bool), typeof(SavedSecretMask), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public PasswordBox? Target
    {
        get => (PasswordBox?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public bool IsSaved
    {
        get => (bool)GetValue(IsSavedProperty);
        set => SetValue(IsSavedProperty, value);
    }

    public SavedSecretMask()
    {
        IsHitTestVisible = false; Focusable = false;
    }

    // Shown only for an existing credential and an empty input; typing hides it immediately.
    public bool IsShowing => IsSaved && Target is { } target && IsEmpty(target);

    private static bool IsEmpty(PasswordBox box)
    {
        using var value = box.SecurePassword;
        return value.Length == 0;
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var mask = (SavedSecretMask)d;
        if (e.OldValue is PasswordBox old)
        {
            old.PasswordChanged -= mask.Redraw; old.SizeChanged -= mask.Redraw; old.Loaded -= mask.Redraw;
        }
        if (e.NewValue is PasswordBox box)
        {
            box.PasswordChanged += mask.Redraw; box.SizeChanged += mask.Redraw; box.Loaded += mask.Redraw;
        }
        mask.InvalidateVisual();
    }

    private void Redraw(object sender, RoutedEventArgs e) => InvalidateVisual();

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    protected override void OnRender(DrawingContext context)
    {
        if (!IsShowing || Target is not { } box || box.Template?.FindName("PART_ContentHost", box) is not FrameworkElement host ||
            !host.IsVisible || !box.IsVisible) return;
        // Text is laid out by the internal TextBoxView inside the content host.
        var view = FindTextView(host) ?? host;
        var origin = view.TransformToVisual(this).Transform(new Point(0, 0));
        var clip = host.TransformToVisual(this).TransformBounds(new Rect(host.RenderSize));
        var brush = TryFindResource("TextControlPlaceholderForeground") as Brush ??
            TryFindResource("TextPlaceholderColorBrush") as Brush ?? Brushes.Gray;
        var text = new FormattedText(new string(box.PasswordChar, GlyphCount), CultureInfo.CurrentUICulture,
            box.FlowDirection, new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
            box.FontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.PushClip(new RectangleGeometry(clip));
        context.DrawText(text, origin);
        context.Pop();
    }

    private static FrameworkElement? FindTextView(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && child.GetType().Name == "TextBoxView") return element;
            if (FindTextView(child) is { } found) return found;
        }
        return null;
    }
}
