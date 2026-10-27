namespace LinkPeek.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Per-monitor V2 for the whole process: WPF lays out in DIPs and rescales per display.
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        if (args is ["--render-ui", var directory]) { UiPreview.Render(directory); return; }
        using var instance = new Mutex(true, @"Local\LinkPeek.Windows", out var first);
        if (!first) return;
        var app = App.Instance;
        using var tray = new TrayApplication(app);
        app.Run();
    }
}
