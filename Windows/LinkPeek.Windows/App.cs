using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace LinkPeek.Windows;

// WPF application shell. WPF UI supplies the Fluent theme resources (Mica, NavigationView,
// CardControl, ToggleSwitch, InfoBar) used by every LinkPeek window.
internal sealed class App : Application
{
    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        Resources.MergedDictionaries.Add(new ControlsDictionary());
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LinkPeek;component/Styles.xaml") });
        // Settings rows: WPF UI's CardControl with the 4 px stacking and 68 px height of Windows settings cards.
        var card = new Style(typeof(Wpf.Ui.Controls.CardControl), (Style)FindResource(typeof(Wpf.Ui.Controls.CardControl)));
        card.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)));
        card.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 68.0));
        card.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(16, 12, 16, 12)));
        Resources["SettingsCardStyle"] = card;
    }

    public static App Instance => Current as App ?? new App();

    // Synthetic previews pin the theme and Windows' default accent so CI output is stable.
    public static bool PreviewMode { get; set; }

    public static void FollowSystemTheme()
    {
        if (!PreviewMode) ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
    }

    public static void ApplyPreviewTheme(bool dark)
    {
        var theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x00, 0x78, 0xD4), theme);
    }

    // Lazy: pack:// URIs resolve only after the Application exists.
    private static BitmapImage? logo;
    public static BitmapImage Logo => logo ??= LoadLogo();

    private static BitmapImage LoadLogo()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = Asset("AppIcon.png");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // Absolute component URIs also resolve when DesktopChecks hosts these windows.
    public static Uri Asset(string name) => new($"pack://application:,,,/LinkPeek;component/Assets/{name}");

    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
