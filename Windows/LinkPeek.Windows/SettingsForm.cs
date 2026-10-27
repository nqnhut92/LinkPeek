namespace LinkPeek.Windows;

internal sealed class SettingsForm : Form
{
    private readonly TextBox organization = new() { Width = 380 };
    private readonly TextBox token = new() { Width = 380, UseSystemPasswordChar = true };
    private readonly Label credential = new() { AutoSize = true };
    private readonly Label message = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly NumericUpDown delay = new() { Minimum = 300, Maximum = 1000, Increment = 50, Width = 100 };
    private readonly CheckBox alt = new() { Text = "Require Alt + hover", AutoSize = true };
    private readonly CheckBox skip = new() { Text = "Skip confirmation: Approve immediately submits a vote", AutoSize = true };
    private readonly CheckBox notify = new() { Text = "Notify after the server verifies approval", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Launch at Windows sign-in", AutoSize = true };
    private readonly Preferences settings;
    private readonly Action changed;
    private readonly FlowLayoutPanel content = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(24)
    };
    private string Org => organization.Text.Trim().ToLowerInvariant();

    public SettingsForm(Preferences settings, Action changed)
    {
        this.settings = settings; this.changed = changed;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "LinkPeek · Settings"; ClientSize = new Size(510, 650); MinimumSize = new Size(530, 690);
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(content);
        Heading("General");
        Add(new Label { Text = "Hover delay (ms)", AutoSize = true }); Add(delay); Add(alt); Add(startup);
        Heading("Azure DevOps");
        Add(new Label { Text = "Organization name (not its URL)", AutoSize = true }); Add(organization);
        Add(new Label { Text = "PAT · Code (Read & Write) for approval", AutoSize = true }); Add(token); Add(credential);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        Button AddButton(string title, EventHandler handler)
        {
            var button = new Button { Text = title, AutoSize = true }; button.Click += handler; buttons.Controls.Add(button); return button;
        }
        AddButton("Save PAT", (_, _) => SaveToken());
        AddButton("Remove PAT", (_, _) => RemoveToken());
        var test = AddButton("Test connection", async (sender, _) =>
        {
            var button = (Button)sender!; button.Enabled = false;
            try
            {
                using var client = new AzureDevOpsClient(Org, CredentialStore.Load(Org) ?? "");
                await client.IdentityAsync(CancellationToken.None);
                if (!IsDisposed) message.Text = "Connected. Authenticated reviewer verified. No vote was sent.";
            }
            catch (Exception error) { if (!IsDisposed) message.Text = CardForm.SafeMessage(error); }
            finally { if (!IsDisposed) button.Enabled = true; }
        });
        Add(buttons); Add(skip); Add(notify);
        Add(new Label { Text = "PATs stay in Windows Credential Manager. Hovering never votes.", AutoSize = true, MaximumSize = new Size(420, 0) });
        Add(new Label { Text = "Apps must expose full link targets through UI Automation. Use Open link… in the tray menu if a target is unavailable.", AutoSize = true, MaximumSize = new Size(420, 0) });
        var save = new Button { Text = "Save settings", AutoSize = true, Margin = new Padding(3, 16, 3, 3) };
        save.Click += (_, _) => SaveSettings(); Add(save); Add(message);
        delay.Value = settings.DelayMs; alt.Checked = settings.AltHover; skip.Checked = settings.SkipConfirmation;
        notify.Checked = settings.SuccessNotification; organization.Text = settings.Organization;
        try { startup.Checked = LoginStartup.Enabled; } catch (System.Security.SecurityException) { startup.Enabled = false; }
        organization.TextChanged += (_, _) => { token.Clear(); UpdateCredential(); };
        FormClosed += (_, _) => token.Clear(); UpdateCredential();
    }

    private void Add(Control control) => content.Controls.Add(control);
    private void Heading(string text) => Add(new Label { Text = text, AutoSize = true,
        Font = new Font("Segoe UI", 13, FontStyle.Bold), Margin = new Padding(3, 12, 3, 10) });
    private void UpdateCredential()
    {
        try { credential.Text = PullRequestLink.ValidOrganization(Org) && CredentialStore.Contains(Org)
            ? "PAT saved in Windows Credential Manager" : "No PAT saved for this organization"; }
        catch { credential.Text = "Cannot access Windows Credential Manager"; }
    }
    private void SaveToken()
    {
        try
        {
            CredentialStore.Save(Org, token.Text); token.Clear();
            settings.Organization = Org; settings.Save(); changed(); UpdateCredential();
            message.Text = "PAT saved to Windows Credential Manager.";
        }
        catch (Exception error) { message.Text = CardForm.SafeMessage(error); }
    }
    private void RemoveToken()
    {
        try
        {
            CredentialStore.Remove(Org); token.Clear();
            if (settings.Organization == Org) settings.Organization = "";
            settings.Save(); changed(); UpdateCredential(); message.Text = "PAT removed.";
        }
        catch (Exception error) { message.Text = CardForm.SafeMessage(error); }
    }
    private void SaveSettings()
    {
        try
        {
            if (Org.Length > 0 && !PullRequestLink.ValidOrganization(Org)) throw new UserError("Enter an organization name, not a URL.");
            if (startup.Enabled) LoginStartup.Set(startup.Checked);
            settings.Organization = Org; settings.DelayMs = (int)delay.Value; settings.AltHover = alt.Checked;
            settings.SkipConfirmation = skip.Checked; settings.SuccessNotification = notify.Checked;
            settings.Save(); changed(); message.Text = "Settings saved.";
        }
        catch (Exception error) { message.Text = error is UserError ? error.Message : "Could not save settings or sign-in registration. Company policy may restrict this option."; }
    }
}
