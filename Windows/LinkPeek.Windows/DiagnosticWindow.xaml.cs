using System.Runtime.InteropServices;
using System.Windows;
using Wpf.Ui.Controls;

namespace LinkPeek.Windows;

internal sealed partial class DiagnosticWindow : FluentWindow
{
    public DiagnosticWindow(string report)
    {
        InitializeComponent();
        TitleIcon.Source = App.Logo;
        if (App.PreviewMode) WindowBackdropType = WindowBackdropType.None;
        Report.Text = report;
        Copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(report); CopyStatus.Text = "Copied"; }
            catch (ExternalException) { CopyStatus.Text = "Clipboard is busy. Try again."; }
        };
        CloseReport.Click += (_, _) => Close();
    }
}
