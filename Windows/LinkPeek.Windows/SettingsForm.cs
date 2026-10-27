namespace LinkPeek.Windows;

internal sealed class SettingsForm : ModernForm
{
    private readonly TextBox organization = new() { BorderStyle = BorderStyle.None };
    private readonly TextBox token = new() { BorderStyle = BorderStyle.None, UseSystemPasswordChar = true };
    private readonly Label credential = UI.Label("No PAT saved", 9, secondary: true);
    private readonly Label message = UI.Label("", 9, secondary: true, width: 540);
    private readonly TrackBar delay = new() { Minimum = 300, Maximum = 1000, TickFrequency = 50,
        SmallChange = 50, LargeChange = 100, TickStyle = TickStyle.None, AutoSize = false, Height = 32 };
    private readonly Label delayLabel = UI.Label("500 ms", 10, bold: true);
    private readonly ToggleSwitch alt = new() { AccessibleName = "Require Alt + hover" };
    private readonly ToggleSwitch skip = new() { AccessibleName = "Skip approval confirmation" };
    private readonly ToggleSwitch notify = new() { AccessibleName = "Success notification" };
    private readonly ToggleSwitch startup = new() { AccessibleName = "Launch at sign-in" };
    private readonly Preferences settings;
    private readonly Action changed;
    private readonly bool preview;
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 16) };
    private readonly List<ModernButton> navigation = [];
    private string Org => organization.Text.Trim().ToLowerInvariant();

    public SettingsForm(Preferences settings, Action changed, bool preview = false)
    {
        this.settings = settings; this.changed = changed; this.preview = preview;
        Text = "LinkPeek"; ClientSize = new Size(840, 670); MinimumSize = new Size(820, 650);
        StartPosition = FormStartPosition.CenterScreen;
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 204, BackColor = Colors.Sidebar,
            Padding = new Padding(18, 22, 18, 16) };
        var sideItems = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Colors.Sidebar };
        var logo = UI.Logo(48); logo.Margin = new Padding(10, 0, 0, 14); sideItems.Controls.Add(logo);
        var brand = UI.Label("LinkPeek", 19, bold: true, width: 164);
        brand.Margin = new Padding(10, 4, 0, 0); sideItems.Controls.Add(brand);
        var subtitle = UI.Label("A little more behind every link.", 8.5f, true, width: 160);
        subtitle.Margin = new Padding(10, 6, 0, 28); sideItems.Controls.Add(subtitle);
        AddNav("General", "◉   General", GeneralPage);
        AddNav("Azure DevOps", "◇   Azure DevOps", AzurePage);
        AddNav("About", "ⓘ   About", AboutPage);
        var version = UI.Label("WINDOWS  ·  " + AppVersion, 8, true);
        version.Dock = DockStyle.Bottom; version.Padding = new Padding(10, 8, 0, 0); version.Height = 28;
        sidebar.Controls.Add(sideItems); sidebar.Controls.Add(version);
        Controls.Add(pageHost); Controls.Add(sidebar);
        delay.Value = settings.DelayMs; delayLabel.Text = $"{delay.Value} ms";
        delay.ValueChanged += (_, _) => delayLabel.Text = $"{delay.Value} ms";
        alt.Checked = settings.AltHover; skip.Checked = settings.SkipConfirmation; notify.Checked = settings.SuccessNotification;
        organization.Text = settings.Organization;
        if (!preview)
        {
            try { startup.Checked = LoginStartup.Enabled; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
            { startup.Enabled = false; }
        }
        organization.TextChanged += (_, _) => { token.Clear(); UpdateCredential(); };
        FormClosed += (_, _) => token.Clear(); UpdateCredential();
        ShowPage(GeneralPage, navigation[0]);

        void AddNav(string name, string label, Func<FlowLayoutPanel> create)
        {
            var nav = new ModernButton { Text = label, AccessibleName = name, Quiet = true,
                AutoSize = false, Size = new Size(168, 42), Margin = new Padding(0, 0, 0, 7) };
            nav.Click += (_, _) => ShowPage(create, nav); navigation.Add(nav); sideItems.Controls.Add(nav);
        }
    }

    internal static string AppVersion => typeof(SettingsForm).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    private void ShowPage(Func<FlowLayoutPanel> create, ModernButton selected)
    {
        foreach (Control control in new Control[] { organization, token, credential, message, delay, delayLabel, alt, skip, notify, startup })
            control.Parent?.Controls.Remove(control);
        var old = pageHost.Controls.Cast<Control>().ToArray();
        pageHost.Controls.Clear(); foreach (var control in old) control.Dispose();
        foreach (var nav in navigation) { nav.Selected = nav == selected; nav.Invalidate(); }
        var page = create(); pageHost.Controls.Add(page);
        void ResizePage()
        {
            foreach (Control control in page.Controls)
            {
                control.Width = Math.Max(300, page.ClientSize.Width - 20);
                if (control is Label label) label.MaximumSize = new Size(control.Width, 0);
            }
        }
        page.ClientSizeChanged += (_, _) => ResizePage(); ResizePage();
    }

    private FlowLayoutPanel Page(string title, string subtitle)
    {
        var page = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Colors.Background };
        page.Controls.Add(UI.Label(title, 24, bold: true, width: 540));
        var intro = UI.Label(subtitle, 10, true, width: 540); intro.Margin = new Padding(0, 0, 0, 24); page.Controls.Add(intro);
        return page;
    }

    private SurfacePanel Card(FlowLayoutPanel page, int height)
    {
        var card = new SurfacePanel { Width = 540, Height = height }; page.Controls.Add(card); return card;
    }

    private static void ToggleRow(Control parent, int top, string title, string description, ToggleSwitch toggle)
    {
        var row = new TableLayoutPanel { Location = new Point(20, top), Width = parent.Width - 40, Height = 60,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, ColumnCount = 2, BackColor = Color.Transparent };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        var text = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        text.Controls.Add(UI.Label(title, 10, bold: true, width: 390));
        text.Controls.Add(UI.Label(description, 8.5f, true, width: 390));
        toggle.Anchor = AnchorStyles.Top | AnchorStyles.Right; toggle.Margin = new Padding(0, 6, 0, 0);
        row.Controls.Add(text, 0, 0); row.Controls.Add(toggle, 1, 0); parent.Controls.Add(row);
    }

    private FlowLayoutPanel GeneralPage()
    {
        var page = Page("General", "Make LinkPeek feel at home on Windows.");
        var launch = Card(page, 100); ToggleRow(launch, 20, "Launch at sign-in", "Ready when you start your day.", startup);
        var hover = Card(page, 202); ToggleRow(hover, 20, "Alt + hover", "Show cards only while holding Alt.", alt);
        var caption = UI.Label("Hover delay", 10, bold: true); caption.Location = new Point(20, 102); hover.Controls.Add(caption);
        delayLabel.Location = new Point(hover.Width - 82, 102); delayLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        hover.Controls.Add(delayLabel);
        delay.Location = new Point(12, 130); delay.Width = hover.Width - 24;
        delay.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; delay.BackColor = Colors.Surface; hover.Controls.Add(delay);
        var help = UI.Label("PR links show a preview. Other web links become a QR code.\nCards follow your Windows light or dark app theme.", 9, true, width: 540);
        help.Margin = new Padding(0, 4, 0, 16); page.Controls.Add(help); AddSave(page); return page;
    }

    private Control Input(TextBox input, string accessibleName)
    {
        input.AccessibleName = accessibleName; input.Font = UI.TextFont(11);
        input.BackColor = Colors.Background; input.ForeColor = Colors.Text;
        var frame = new Panel { Height = 40, Width = 490, Padding = new Padding(11, 10, 11, 7), BackColor = Colors.Background };
        input.Dock = DockStyle.Fill; frame.Controls.Add(input);
        frame.Paint += (_, e) => { using var pen = new Pen(Colors.Border); e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1); };
        return frame;
    }

    private FlowLayoutPanel AzurePage()
    {
        var page = Page("Azure DevOps", "Your pull requests, right where you hover.");
        var connection = Card(page, 304);
        var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Colors.Surface };
        fields.Controls.Add(UI.Label("CONNECTION", 8, true, true));
        fields.Controls.Add(UI.Label("Organization", 10, bold: true)); fields.Controls.Add(Input(organization, "Organization name"));
        var patLabel = UI.Label("Personal access token", 10, bold: true); patLabel.Margin = new Padding(0, 14, 0, 6);
        fields.Controls.Add(patLabel); fields.Controls.Add(Input(token, "Personal access token"));
        credential.Margin = new Padding(0, 7, 0, 12); fields.Controls.Add(credential);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Colors.Surface };
        buttons.Controls.Add(UI.Button("Save PAT", SaveToken, true)); buttons.Controls.Add(UI.Button("Remove", RemoveToken));
        buttons.Controls.Add(UI.Button("Test connection", async () => await TestConnection())); fields.Controls.Add(buttons);
        fields.SizeChanged += (_, _) => { foreach (Control c in fields.Controls) if (c is Panel && c != buttons) c.Width = fields.ClientSize.Width - 4; };
        connection.Controls.Add(fields);
        var approval = Card(page, 162);
        ToggleRow(approval, 18, "Skip confirmation", "An Approve click submits after verification.", skip);
        ToggleRow(approval, 88, "Success notification", "Notify after the server confirms your vote.", notify);
        page.Controls.Add(UI.Label("PATs stay in Windows Credential Manager. Use Code (Read & Write)\nfor approval. Hovering never casts a vote.", 9, true, width: 540));
        AddSave(page); return page;
    }

    private FlowLayoutPanel AboutPage()
    {
        var page = Page("About", "A little more behind every link.");
        var card = Card(page, 280);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Colors.Surface };
        content.Controls.Add(UI.Label("LinkPeek", 30, bold: true));
        content.Controls.Add(UI.Label($"Version {AppVersion}  ·  Windows", 10, true));
        var description = UI.Label("Turn web links into shareable QR codes.\nPreview and approve Azure DevOps pull requests\nwithout losing your place.", 11, width: 470);
        description.Margin = new Padding(0, 22, 0, 20); content.Controls.Add(description);
        var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Colors.Surface };
        buttons.Controls.Add(UI.Button("GitHub ↗", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://github.com/nqnhut92/LinkPeek") { UseShellExecute = true }), true));
        buttons.Controls.Add(UI.Button("MIT License", () => MessageBox.Show(this,
            File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE")) ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LICENSE")) : "See LICENSE alongside LinkPeek.exe.", "MIT License")));
        content.Controls.Add(buttons); card.Controls.Add(content);
        page.Controls.Add(UI.Label("Built for the Windows tray. QR generation stays on your device.\nLink detection depends on each app's accessibility support.", 9, true, width: 540)); return page;
    }

    private void AddSave(FlowLayoutPanel page)
    {
        var footer = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 8) };
        footer.Controls.Add(UI.Button("Save settings", SaveSettings, true)); page.Controls.Add(footer); page.Controls.Add(message);
    }

    internal void PreviewPage(string page)
    {
        if (page == "azure") ShowPage(AzurePage, navigation[1]);
        else if (page == "about") ShowPage(AboutPage, navigation[2]);
    }

    private void UpdateCredential()
    {
        if (preview) { credential.Text = "No PAT saved · Windows Credential Manager"; return; }
        try { credential.Text = PullRequestLink.ValidOrganization(Org) && CredentialStore.Contains(Org)
            ? "✓  PAT saved in Windows Credential Manager" : "No PAT saved for this organization"; }
        catch { credential.Text = "Cannot access Windows Credential Manager"; }
    }
    private async Task TestConnection()
    {
        if (preview) return;
        try
        {
            using var client = new AzureDevOpsClient(Org, CredentialStore.Load(Org) ?? "");
            message.Text = "Connecting…"; await client.IdentityAsync(CancellationToken.None);
            if (!IsDisposed) message.Text = "✓  Connected. Reviewer verified. No vote was sent.";
        }
        catch (Exception error) { if (!IsDisposed) message.Text = CardForm.SafeMessage(error); }
    }
    private void SaveToken()
    {
        if (preview) return;
        try
        {
            CredentialStore.Save(Org, token.Text); token.Clear(); settings.Organization = Org; settings.Save();
            changed(); UpdateCredential(); message.Text = "✓  PAT saved to Windows Credential Manager.";
        }
        catch (Exception error) { message.Text = CardForm.SafeMessage(error); }
    }
    private void RemoveToken()
    {
        if (preview) return;
        try
        {
            CredentialStore.Remove(Org); token.Clear(); if (settings.Organization == Org) settings.Organization = "";
            settings.Save(); changed(); UpdateCredential(); message.Text = "PAT removed.";
        }
        catch (Exception error) { message.Text = CardForm.SafeMessage(error); }
    }
    private void SaveSettings()
    {
        if (preview) return;
        try
        {
            if (Org.Length > 0 && !PullRequestLink.ValidOrganization(Org)) throw new UserError("Enter an organization name, not a URL.");
            if (startup.Enabled) LoginStartup.Set(startup.Checked);
            settings.Organization = Org; settings.DelayMs = delay.Value; settings.AltHover = alt.Checked;
            settings.SkipConfirmation = skip.Checked; settings.SuccessNotification = notify.Checked;
            settings.Save(); changed(); message.Text = "✓  Settings saved.";
        }
        catch (Exception error) { message.Text = error is UserError ? error.Message : "Could not save settings or sign-in registration. Company policy may restrict this option."; }
    }
}
