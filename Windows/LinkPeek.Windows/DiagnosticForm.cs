namespace LinkPeek.Windows;

internal sealed class DiagnosticForm : ModernForm
{
    public DiagnosticForm(string report)
    {
        Text = "LinkPeek · Link diagnostics"; ClientSize = new Size(660, 490); Padding = new Padding(24);
        StartPosition = FormStartPosition.CenterScreen;
        var heading = UI.Label("Link diagnostics", 20, bold: true); heading.Dock = DockStyle.Top; heading.Height = 44;
        var details = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
            WordWrap = false, Text = report, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Colors.Surface, ForeColor = Colors.Text, Font = new Font("Consolas", 9) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(0, 12, 0, 0) };
        var copy = UI.Button("Copy report", () =>
        {
            try { Clipboard.SetText(report); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "Clipboard is busy. Try again.", "LinkPeek"); }
        }, true);
        buttons.Controls.Add(copy); buttons.Controls.Add(UI.Button("Close", Close));
        Controls.Add(details); Controls.Add(heading); Controls.Add(buttons);
    }
}
