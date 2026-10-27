using System.Windows;
using Wpf.Ui.Controls;

namespace LinkPeek.Windows;

internal sealed partial class OpenLinkWindow : FluentWindow
{
    public WebLink? Link { get; private set; }

    public OpenLinkWindow()
    {
        InitializeComponent();
        TitleIcon.Source = App.Logo;
        if (App.PreviewMode) WindowBackdropType = WindowBackdropType.None;
        Input.TextChanged += (_, _) => Error.Visibility = Visibility.Collapsed;
        Open.Click += (_, _) =>
        {
            Link = WebLink.Parse(Input.Text.Trim());
            if (Link is null) { Error.Visibility = Visibility.Visible; Input.Focus(); return; }
            DialogResult = true;
        };
        Loaded += (_, _) => Input.Focus();
    }

    internal void PreviewInvalid(string text)
    {
        Input.Text = text; Error.Visibility = Visibility.Visible;
    }
}
