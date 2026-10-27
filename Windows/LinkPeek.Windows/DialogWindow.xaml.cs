using System.Windows;
using Wpf.Ui.Controls;

namespace LinkPeek.Windows;

internal sealed partial class DialogWindow : FluentWindow
{
    private DialogWindow(Window? owner, string heading, string message, string primary, string? secondary, bool primaryIsDefault)
    {
        InitializeComponent();
        if (owner is { IsLoaded: true }) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        if (App.PreviewMode) WindowBackdropType = WindowBackdropType.None;
        Heading.Text = heading; Message.Text = message;
        Message.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Primary.Content = primary;
        Primary.Click += (_, _) => { DialogResult = true; };
        if (secondary is null) { Secondary.Visibility = Visibility.Collapsed; Primary.IsCancel = true; Primary.IsDefault = true; }
        else
        {
            Secondary.Content = secondary;
            // Consequential actions (approve, remove) default to the safe button, as WinUI dialogs do.
            if (primaryIsDefault) Primary.IsDefault = true; else Secondary.IsDefault = true;
        }
        Loaded += (_, _) => (primaryIsDefault || secondary is null ? Primary : Secondary).Focus();
    }

    public static bool Confirm(Window? owner, string heading, string message, string primary, string secondary,
        bool primaryIsDefault = false) =>
        new DialogWindow(owner, heading, message, primary, secondary, primaryIsDefault).ShowDialog() == true;

    public static void ShowText(Window? owner, string heading, string text)
    {
        var dialog = new DialogWindow(owner, heading, "", "Close", null, true) { Width = 600 };
        dialog.LongText.Text = text; dialog.LongText.Visibility = Visibility.Visible;
        dialog.ShowDialog();
    }

    internal static DialogWindow PreviewConfirmation() => new(null, "Approve PR #142?",
        "Make links feel at home on Windows\n\nCommit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n\n" +
        "LinkPeek will recheck this commit and your identity before submitting one vote.", "Approve", "Cancel", false);
}
