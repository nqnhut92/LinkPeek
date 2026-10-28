using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace LinkPeek.Windows;

internal enum SettingsPage { General, AzureDevOps, About }

internal sealed partial class SettingsWindow : FluentWindow, INavigationViewPageProvider
{
    private readonly Preferences settings;
    private readonly Action changed;
    private readonly bool preview;
    private readonly Func<string, bool> credentialExists;
    private readonly GeneralPage general = new();
    private readonly AzureDevOpsPage azure = new();
    private readonly AboutPage about = new();
    private readonly DispatcherTimer delaySave = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool loading = true;
    private SettingsPage pendingPage = SettingsPage.General;
    private string Org => azure.Organization.Text.Trim().ToLowerInvariant();

    internal static string AppVersion => typeof(SettingsWindow).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    // Preview mode never touches the registry, Credential Manager, settings.json or the network.
    public SettingsWindow(Preferences settings, Action changed, bool preview = false, Func<string, bool>? credentialExists = null)
    {
        this.settings = settings; this.changed = changed; this.preview = preview;
        this.credentialExists = credentialExists ?? CredentialStore.Contains;
        InitializeComponent();
        TitleIcon.Source = App.Logo;
        Navigation.SetPageProviderService(this);
        if (preview) { WindowBackdropType = WindowBackdropType.None; Navigation.Transition = Wpf.Ui.Animations.Transition.None; }
        else Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        general.Delay.Value = settings.DelayMs; general.DelayValue.Text = $"{settings.DelayMs} ms";
        general.Alt.IsChecked = settings.AltHover;
        azure.Skip.IsChecked = settings.SkipConfirmation; azure.Notify.IsChecked = settings.SuccessNotification;
        azure.Organization.Text = settings.Organization;
        if (!preview)
        {
            try { general.Startup.IsChecked = LoginStartup.Enabled; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
            { general.Startup.IsEnabled = false; Show(general.Status, InfoBarSeverity.Warning, "Sign-in startup is managed by policy on this PC."); }
        }
        about.Logo.Source = App.Logo;
        about.VersionText.Text = $"Version {AppVersion} · Windows · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

        general.Startup.Click += (_, _) => SetStartup();
        general.Alt.Click += (_, _) => Persist(() => settings.AltHover = general.Alt.IsChecked == true, general.Status);
        general.Delay.ValueChanged += (_, e) =>
        {
            general.DelayValue.Text = $"{(int)e.NewValue} ms";
            if (loading) return;
            settings.DelayMs = (int)e.NewValue; delaySave.Stop(); delaySave.Start();
        };
        delaySave.Tick += (_, _) => { delaySave.Stop(); Persist(() => { }, general.Status); };
        azure.Skip.Click += (_, _) => Persist(() => settings.SkipConfirmation = azure.Skip.IsChecked == true, azure.Status);
        azure.Notify.Click += (_, _) => Persist(() => settings.SuccessNotification = azure.Notify.IsChecked == true, azure.Status);
        azure.Organization.TextChanged += (_, _) => { azure.Token.Clear(); UpdateCredential(); };
        azure.Organization.LostKeyboardFocus += (_, _) => SaveOrganization();
        azure.Organization.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveOrganization(); };
        azure.Token.PasswordChanged += (_, _) => UpdateButtons();
        azure.SaveToken.Click += (_, _) => SaveToken();
        azure.RemoveToken.Click += (_, _) => RemoveToken();
        azure.TestConnection.Click += async (_, _) => await TestConnectionAsync();
        about.License.Click += (_, _) => ShowTextFile("MIT License", "LICENSE");
        about.Notices.Click += (_, _) => ShowTextFile("Third-party notices", "THIRD-PARTY-NOTICES.txt");
        Closing += (_, _) =>
        {
            if (delaySave.IsEnabled) { delaySave.Stop(); Persist(() => { }, general.Status); }
            // UnWatch needs the window handle, which is already destroyed by the time Closed fires.
            if (!preview) StopWatchingTheme();
        };
        Closed += (_, _) => azure.Token.Clear();
        UpdateCredential();
        loading = false;
        // NavigationView can navigate only after its template is applied.
        Loaded += (_, _) => ShowPage(pendingPage);
        // Like WinUI's NavigationView Auto mode: collapse to icons when the window is narrow.
        SizeChanged += (_, _) => Navigation.IsPaneOpen = ActualWidth >= 900;
    }

    private void StopWatchingTheme()
    {
        try { Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(this); }
        catch (InvalidOperationException) { /* Closed before the window handle existed. */ }
    }

    object? INavigationViewPageProvider.GetPage(Type pageType) =>
        pageType == typeof(GeneralPage) ? general : pageType == typeof(AzureDevOpsPage) ? azure :
        pageType == typeof(AboutPage) ? about : null;

    public void ShowPage(SettingsPage page)
    {
        pendingPage = page;
        if (!IsLoaded) return;
        Navigation.Navigate(page switch
        {
            SettingsPage.AzureDevOps => typeof(AzureDevOpsPage),
            SettingsPage.About => typeof(AboutPage),
            _ => typeof(GeneralPage)
        });
    }

    internal FrameworkElement Page(SettingsPage page) => page switch
    {
        SettingsPage.AzureDevOps => azure, SettingsPage.About => about, _ => general
    };

    internal AzureDevOpsPage AzurePage => azure;

    private static void Show(InfoBar bar, InfoBarSeverity severity, string message, string title = "")
    {
        bar.Severity = severity; bar.Title = title; bar.Message = message; bar.IsOpen = true;
    }

    private void Persist(Action update, InfoBar status)
    {
        if (loading) return;
        update();
        if (preview) return;
        try { settings.Save(); status.IsOpen = false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Show(status, InfoBarSeverity.Error, "Could not save settings. Check access to %LOCALAPPDATA%\\LinkPeek."); }
    }

    private void SetStartup()
    {
        if (preview) return;
        var wanted = general.Startup.IsChecked == true;
        try { LoginStartup.Set(wanted); general.Status.IsOpen = false; }
        catch
        {
            general.Startup.IsChecked = !wanted;
            Show(general.Status, InfoBarSeverity.Error, "Could not change sign-in registration. Company policy may restrict this option.");
        }
    }

    private void SaveOrganization()
    {
        if (loading || preview || Org == settings.Organization) return;
        if (Org.Length > 0 && !PullRequestLink.ValidOrganization(Org))
        { Show(azure.Status, InfoBarSeverity.Error, "Enter an organization name, not a URL."); return; }
        Persist(() => settings.Organization = Org, azure.Status);
        changed();
    }

    private bool HasSavedCredential()
    {
        if (!PullRequestLink.ValidOrganization(Org)) return false;
        return credentialExists(Org);
    }

    // Only existence is checked. The stored PAT is never read to update this view.
    private void UpdateCredential()
    {
        bool saved;
        try { saved = HasSavedCredential(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or UserError)
        {
            azure.SavedMask.IsSaved = false;
            SetCredential(SymbolRegular.ErrorCircle24, "Cannot access Windows Credential Manager", "Saved PATs are unavailable right now.", false);
            return;
        }
        azure.SavedMask.IsSaved = saved;
        if (saved)
            SetCredential(SymbolRegular.CheckmarkCircle24, "PAT saved", $"Stored in Windows Credential Manager for {Org}. Type a new PAT to replace it.", true);
        else if (Org.Length == 0)
            SetCredential(SymbolRegular.LockShield24, "No organization", "Enter your organization name first.", false);
        else if (!PullRequestLink.ValidOrganization(Org))
            SetCredential(SymbolRegular.ErrorCircle24, "Check the organization", "Use only the name, for example contoso, not a URL.", false);
        else
            SetCredential(SymbolRegular.LockShield24, "No PAT saved", $"Enter a PAT, then save it for {Org}.", false);
        AutomationProperties.SetHelpText(azure.Token, saved
            ? "A PAT is already saved for this organization. Leave empty to keep it, or type a new PAT to replace it."
            : "Enter a personal access token.");
        UpdateButtons();
    }

    private void SetCredential(SymbolRegular symbol, string title, string detail, bool saved)
    {
        azure.CredentialIcon.Symbol = symbol;
        azure.CredentialIcon.Filled = saved;
        azure.CredentialIcon.SetResourceReference(ForegroundProperty, saved ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
        azure.CredentialTitle.Text = title; azure.CredentialDetail.Text = detail;
        azure.RemoveToken.Tag = saved;
    }

    private void UpdateButtons()
    {
        var valid = PullRequestLink.ValidOrganization(Org);
        var saved = azure.RemoveToken.Tag is true;
        using var entered = azure.Token.SecurePassword;
        azure.SaveToken.IsEnabled = valid && entered.Length > 0;
        azure.TestConnection.IsEnabled = saved;
        azure.RemoveToken.IsEnabled = saved;
    }

    private async Task TestConnectionAsync()
    {
        if (preview) return;
        azure.TestConnection.IsEnabled = false;
        try
        {
            using var client = new AzureDevOpsClient(Org, CredentialStore.Load(Org) ?? "");
            Show(azure.Status, InfoBarSeverity.Informational, "Connecting to Azure DevOps…");
            await client.IdentityAsync(CancellationToken.None);
            if (IsLoaded) Show(azure.Status, InfoBarSeverity.Success, "Reviewer identity verified. No vote was sent.", "Connected");
        }
        catch (Exception error) { if (IsLoaded) Show(azure.Status, InfoBarSeverity.Error, CardWindow.SafeMessage(error)); }
        finally { if (IsLoaded) UpdateButtons(); }
    }

    private void SaveToken()
    {
        if (preview) return;
        try
        {
            CredentialStore.Save(Org, azure.Token.Password); azure.Token.Clear(); settings.Organization = Org; settings.Save();
            changed(); UpdateCredential();
            Show(azure.Status, InfoBarSeverity.Success, "Your PAT is stored in Windows Credential Manager.", "PAT saved");
        }
        catch (Exception error) { Show(azure.Status, InfoBarSeverity.Error, CardWindow.SafeMessage(error)); }
    }

    private void RemoveToken()
    {
        if (preview) return;
        if (!DialogWindow.Confirm(this, "Remove saved PAT?",
                $"LinkPeek will delete the PAT saved for {Org} from Windows Credential Manager. Pull request previews stop until you save a new one.",
                "Remove", "Cancel")) return;
        try
        {
            CredentialStore.Remove(Org); azure.Token.Clear(); if (settings.Organization == Org) settings.Organization = "";
            settings.Save(); changed(); UpdateCredential();
            Show(azure.Status, InfoBarSeverity.Informational, "The saved PAT was removed.");
        }
        catch (Exception error) { Show(azure.Status, InfoBarSeverity.Error, CardWindow.SafeMessage(error)); }
    }

    private void ShowTextFile(string title, string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        var text = File.Exists(path) ? File.ReadAllText(path) : $"See {file} alongside LinkPeek.exe.";
        DialogWindow.ShowText(this, title, text);
    }
}
