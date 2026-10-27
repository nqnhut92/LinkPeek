namespace LinkPeek.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args is ["--render-ui", var directory]) { UiPreview.Render(directory); return; }
        using var instance = new Mutex(true, @"Local\LinkPeek.Windows", out var first);
        if (!first) return;
        Application.Run(new TrayApplication());
    }
}
