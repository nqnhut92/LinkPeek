namespace LinkPeek.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Per-monitor V2 for the whole process: WPF lays out in DIPs and rescales per display.
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        using var instance = new Mutex(true, @"Local\LinkPeek.Windows", out var first);
        if (!first) return;
        var app = App.Instance;
        using var tray = new TrayApplication(app);
        app.Run();
    }
}
