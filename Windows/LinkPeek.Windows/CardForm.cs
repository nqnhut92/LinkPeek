using QRCoder;

namespace LinkPeek.Windows;

internal sealed class CardForm : Form
{
    private readonly FlowLayoutPanel body = new()
    {
        FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16),
        BackColor = SystemColors.Window
    };
    private readonly Label title = new() { AutoSize = true, MaximumSize = new Size(350, 0), Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(350, 0) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(350, 0), Margin = new Padding(3, 12, 3, 8) };
    private readonly FlowLayoutPanel actions = new() { AutoSize = true, MaximumSize = new Size(355, 0), WrapContents = true };
    private readonly WebLink link;
    private readonly AzureDevOpsClient? client;
    private readonly ApprovalService? approvals;
    private readonly Preferences preferences;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Action copied;
    private readonly Action notify;
    private Snapshot? snapshot;
    private PictureBox? qr;
    private Button? approve;
    private bool busy;
    public bool Busy => busy;
    public bool Manual { get; }
    protected override bool ShowWithoutActivation => !Manual;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x00000080; if (!Manual) p.ExStyle |= 0x08000000; return p; }
    }

    public CardForm(WebLink link, AzureDevOpsClient? client, ApprovalService? approvals,
        Preferences preferences, bool manual, Action copied, Action notify)
    {
        this.link = link; this.client = client; this.approvals = approvals;
        this.preferences = preferences; this.copied = copied; this.notify = notify; Manual = manual;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "LinkPeek"; ShowInTaskbar = false; TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedToolWindow; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.Manual; Font = new Font("Segoe UI", 10);
        BackColor = SystemColors.Window; ForeColor = SystemColors.WindowText;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        body.Controls.Add(title); body.Controls.Add(detail); body.Controls.Add(status); body.Controls.Add(actions);
        Controls.Add(body);
        title.Text = link.PullRequest is { } pr ? $"Pull request #{pr.Id}" : "Share this link";
        detail.Text = link.Text.Length > 180 ? link.Text[..180] + "…" : link.Text;
        AddAction("Copy Link", () => Copy(false));
        if (link.PullRequest is null)
        {
            try
            {
                using var data = QRCodeGenerator.GenerateQrCode(link.Text, QRCodeGenerator.ECCLevel.M);
                using var png = new PngByteQRCode(data);
                var pixels = Math.Max(1, 264 / data.ModuleMatrix.Count);
                using var stream = new MemoryStream(png.GetGraphic(pixels));
                using var image = Image.FromStream(stream);
                qr = new PictureBox { Image = new Bitmap(image), SizeMode = PictureBoxSizeMode.AutoSize,
                    Margin = new Padding(3, 14, 3, 14), BackColor = Color.White };
                body.Controls.Add(qr); body.Controls.SetChildIndex(qr, 2);
                status.Text = "Generated offline · Full URL preserved";
                AddAction("Copy QR", () => Copy(true));
            }
            catch (Exception e) when (e is QRCoder.Exceptions.DataTooLongException or ArgumentException)
            { status.Text = "This URL is too large for a QR code. Copy Link is still available."; }
        }
        else
        {
            approve = AddAction(preferences.SkipConfirmation ? "Approve immediately" : "Approve…", async () => await ApproveAsync());
            approve.Enabled = false;
            AddAction("Refresh", async () => await RefreshAsync());
            if (preferences.SkipConfirmation) status.Text = "Confirmation is off: Approve sends your vote after verification.";
        }
        AddAction("Close", Close);
        Shown += async (_, _) => { if (link.PullRequest is not null) await RefreshAsync(); ClampToScreen(); };
        FormClosed += (_, _) => { lifetime.Cancel(); qr?.Image?.Dispose(); };
    }

    private Button AddAction(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(4) };
        button.Click += (_, _) => action(); actions.Controls.Add(button); return button;
    }

    private void Copy(bool image)
    {
        try
        {
            if (image && qr?.Image is { } bitmap) Clipboard.SetImage(bitmap);
            else Clipboard.SetText(link.Text);
            copied(); Close();
        }
        catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Clipboard is busy. Try again."; }
    }

    private void Render(Snapshot value)
    {
        snapshot = value;
        title.Text = $"#{value.Pr.PullRequestId} · {value.Pr.Title}";
        detail.Text = $"{value.Pr.CreatedBy?.DisplayName ?? "Unknown author"}\n" +
            $"{value.Pr.SourceRefName?.Replace("refs/heads/", "")} → {value.Pr.TargetRefName?.Replace("refs/heads/", "")}";
        var previous = approvals?.ReviewedHead(link.PullRequest!, value);
        status.Text = (previous is not null && previous != value.Head ? "New commits · Review changes before approving.\n" : "") + value.Caption;
        if (preferences.SkipConfirmation && value.Eligible) status.Text += "\nConfirmation is off: clicking Approve submits a vote.";
        if (approve is not null) approve.Enabled = value.Eligible && !busy;
    }

    private async Task RefreshAsync()
    {
        if (busy || IsDisposed) return;
        busy = true; if (approve is not null) approve.Enabled = false;
        try
        {
            if (client is null || approvals is null) throw new UserError("Configure this organization and save a PAT in Settings.");
            status.Text = "Loading…";
            var value = await client.SnapshotAsync(link.PullRequest!, lifetime.Token);
            if (!IsDisposed) Render(value);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!IsDisposed) { snapshot = null; status.Text = SafeMessage(error); } }
        finally
        {
            busy = false;
            if (!IsDisposed) { if (approve is not null) approve.Enabled = snapshot?.Eligible == true; ClampToScreen(); }
        }
    }

    private async Task ApproveAsync()
    {
        if (busy || snapshot is null || approvals is null) return;
        busy = true; approve!.Enabled = false;
        try
        {
            status.Text = "Checking current PR and commit…";
            var prepared = await approvals.PrepareAsync(link.PullRequest!, snapshot, lifetime.Token);
            if (IsDisposed) return;
            if (!preferences.SkipConfirmation && MessageBox.Show(this,
                    $"Approve PR #{snapshot.Pr.PullRequestId}?\n\n{snapshot.Pr.Title}\n\nCommit: {snapshot.Head}\n\n" +
                    "LinkPeek will recheck this commit and your identity before submitting one vote.",
                    "Confirm approval", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            { status.Text = "Approval cancelled."; return; }
            status.Text = "Approving and verifying…";
            var verified = await approvals.ApproveAsync(prepared, lifetime.Token);
            if (!IsDisposed) { Render(verified); if (preferences.SuccessNotification) notify(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsDisposed) { snapshot = null; status.Text = SafeMessage(error) + "\nUse Refresh before trying again."; }
        }
        finally
        {
            busy = false;
            if (!IsDisposed) { approve.Enabled = snapshot?.Eligible == true; ClampToScreen(); }
        }
    }

    internal static string SafeMessage(Exception error) => error is UserError
        ? error.Message : "Operation failed. Check Settings and try again.";

    public void Place(Rectangle anchor, Point point)
    {
        var work = Screen.FromPoint(point).WorkingArea;
        var size = GetPreferredSize(Size.Empty);
        var x = Math.Clamp(point.X, work.Left, Math.Max(work.Left, work.Right - size.Width));
        var y = anchor.Bottom + 6;
        if (y + size.Height > work.Bottom) y = anchor.Top - size.Height - 6;
        Location = new Point(x, Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));
    }

    private void ClampToScreen()
    {
        var work = Screen.FromControl(this).WorkingArea;
        Location = new Point(Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width)),
            Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
    }
}
