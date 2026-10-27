using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LinkPeek.Windows;

internal sealed record Palette(Color Background, Color Surface, Color Sidebar, Color Text, Color Secondary,
    Color Border, Color Accent, Color AccentText, Color Tint, bool Dark)
{
    public static bool? PreviewDark { get; set; }
    public static Palette Current
    {
        get
        {
            if (SystemInformation.HighContrast)
                return new(SystemColors.Window, SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
                    SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText,
                    SystemColors.Control, false);
            var dark = PreviewDark ?? false;
            if (PreviewDark is null)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
                }
                catch (System.Security.SecurityException) { }
            }
            return dark
                ? new(Color.FromArgb(24, 26, 29), Color.FromArgb(34, 37, 41), Color.FromArgb(28, 30, 34),
                    Color.FromArgb(244, 245, 247), Color.FromArgb(164, 172, 184), Color.FromArgb(57, 62, 70),
                    Color.FromArgb(121, 185, 255), Color.FromArgb(13, 35, 56), Color.FromArgb(36, 55, 73), true)
                : new(Color.FromArgb(246, 247, 249), Color.White, Color.FromArgb(238, 241, 245),
                    Color.FromArgb(28, 34, 43), Color.FromArgb(103, 113, 128), Color.FromArgb(224, 229, 236),
                    Color.FromArgb(0, 105, 194), Color.White, Color.FromArgb(226, 239, 252), false);
        }
    }
}

internal static class UI
{
    public static PictureBox Logo(int size)
    {
        using var stream = typeof(UI).Assembly.GetManifestResourceStream("LinkPeek.Windows.AppIcon.png");
        using var image = stream is null ? null : Image.FromStream(stream);
        var box = new PictureBox { Size = new Size(size, size), SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent, TabStop = false };
        if (image is not null) box.Image = new Bitmap(image, size, size);
        box.Disposed += (_, _) => box.Image?.Dispose(); return box;
    }
    public static Font TextFont(float size = 10, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Label Label(string text, float size = 10, bool secondary = false, bool bold = false,
        int width = 440) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(width, 0),
        Font = TextFont(size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = secondary ? Palette.Current.Secondary : Palette.Current.Text,
        BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 6), UseMnemonic = false
    };

    public static ModernButton Button(string text, Action action, bool primary = false)
    {
        var button = new ModernButton { Text = text, Primary = primary };
        button.Click += (_, _) => action(); return button;
    }

    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal class ModernForm : Form
{
    protected readonly Palette Colors = Palette.Current;
    public ModernForm()
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = UI.TextFont(); BackColor = Colors.Background; ForeColor = Colors.Text;
        DoubleBuffered = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var dark = Colors.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            if (!SystemInformation.HighContrast)
            { var backdrop = 2; DwmSetWindowAttribute(Handle, 38, ref backdrop, sizeof(int)); }
        }
    }
}

internal sealed class SurfacePanel : Panel
{
    private readonly Palette colors = Palette.Current;
    public SurfacePanel()
    {
        DoubleBuffered = true; BackColor = Palette.Current.Surface; Padding = new Padding(20);
        Margin = new Padding(0, 0, 0, 14);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? colors.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UI.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
        using var brush = new SolidBrush(BackColor); e.Graphics.FillPath(brush, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UI.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
        using var pen = new Pen(colors.Border);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ModernButton : Button
{
    private readonly Palette colors = Palette.Current;
    [DefaultValue(false)]
    public bool Primary { get; set; }
    [DefaultValue(false)]
    public bool Selected { get; set; }
    [DefaultValue(false)]
    public bool Quiet { get; set; }
    private bool hover;
    private bool pressed;
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(0, 36); Padding = new Padding(14, 7, 14, 7);
        Margin = new Padding(0, 0, 8, 0); Cursor = Cursors.Hand; Font = UI.TextFont(9.5f, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(Math.Max(MinimumSize.Width, text.Width + Padding.Horizontal + 4),
            Math.Max(MinimumSize.Height, text.Height + Padding.Vertical));
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var c = colors;
        e.Graphics.Clear(Parent?.BackColor ?? c.Surface);
        var fill = Primary ? c.Accent : Selected ? c.Tint : Quiet ? Parent?.BackColor ?? c.Surface : c.Surface;
        if (hover && Enabled) fill = ControlPaint.Light(fill, c.Dark ? 0.12f : 0.04f);
        if (pressed && Enabled) fill = ControlPaint.Dark(fill, 0.08f);
        if (!Enabled) fill = c.Background;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UI.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * DeviceDpi / 96f);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, path);
        if (!Quiet && !Primary && !Selected) { using var pen = new Pen(c.Border); e.Graphics.DrawPath(pen, path); }
        var textColor = !Enabled ? c.Secondary : Primary ? c.AccentText : Selected ? c.Accent : c.Text;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), textColor, fill);
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    private readonly Palette colors = Palette.Current;
    public ToggleSwitch()
    {
        AutoSize = false; Size = new Size(46, 28); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var c = colors; e.Graphics.Clear(Parent?.BackColor ?? c.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var track = new RectangleF(2 * scale, 4 * scale, 40 * scale, 20 * scale);
        using var path = UI.Rounded(track, 10 * scale);
        using var brush = new SolidBrush(Checked && Enabled ? c.Accent : c.Border); e.Graphics.FillPath(brush, path);
        using var knob = new SolidBrush(Checked && Enabled ? c.AccentText : c.Secondary);
        e.Graphics.FillEllipse(knob, (Checked ? 25 : 5) * scale, 7 * scale, 14 * scale, 14 * scale);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

internal sealed class MenuColors(Palette colors) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => colors.Surface;
    public override Color ImageMarginGradientBegin => colors.Surface;
    public override Color ImageMarginGradientMiddle => colors.Surface;
    public override Color ImageMarginGradientEnd => colors.Surface;
    public override Color MenuItemSelected => colors.Tint;
    public override Color MenuItemBorder => colors.Tint;
    public override Color MenuBorder => colors.Border;
    public override Color SeparatorDark => colors.Border;
    public override Color SeparatorLight => colors.Surface;
}
