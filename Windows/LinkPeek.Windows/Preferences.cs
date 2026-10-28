using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinkPeek.Windows;

internal sealed record Preferences
{
    public string Organization { get; set; } = "";
    public int DelayMs { get; set; } = HoverDelay.Default;
    public int HoverPrefsVersion { get; set; }
    public bool AltHover { get; set; }
    public bool SkipConfirmation { get; set; }
    public bool SuccessNotification { get; set; }
    public bool QrEnabled { get; set; } = true;
    public bool GrabEnabled { get; set; } = true;
    public bool AnyOtherLinkQr { get; set; }
    public List<string> CustomPatterns { get; set; } = [];
    public bool PrCardEnabled { get; set; } = true;

    [JsonIgnore]
    public LinkFilter LinkFilter => new(QrEnabled, GrabEnabled, CustomPatterns, AnyOtherLink: AnyOtherLinkQr);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkPeek", "settings.json");

    public static Preferences Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? Fresh();
            result.DelayMs = HoverDelay.Resolve(result.DelayMs, result.HoverPrefsVersion);
            result.HoverPrefsVersion = HoverDelay.CurrentVersion;
            result.Organization ??= "";
            result.CustomPatterns = (result.CustomPatterns ?? []).Where(p => LinkPattern.Normalize(p) is not null).ToList();
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return Fresh(); }
    }

    private static Preferences Fresh() => new() { HoverPrefsVersion = HoverDelay.CurrentVersion };

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}

internal static class LoginStartup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("LinkPeek") is string; }
    }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        if (enabled) key.SetValue("LinkPeek", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("LinkPeek", false);
    }
}
