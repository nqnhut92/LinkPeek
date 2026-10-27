using Microsoft.Win32;
using System.Text.Json;

namespace LinkPeek.Windows;

internal sealed record Preferences
{
    public string Organization { get; set; } = "";
    public int DelayMs { get; set; } = 500;
    public bool AltHover { get; set; }
    public bool SkipConfirmation { get; set; }
    public bool SuccessNotification { get; set; }
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkPeek", "settings.json");

    public static Preferences Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
            result.DelayMs = Math.Clamp(result.DelayMs, 300, 1000);
            result.Organization ??= "";
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

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
