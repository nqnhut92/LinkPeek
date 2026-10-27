namespace LinkPeek.Windows;

// %LOCALAPPDATA%\LinkPeek\crash.log: exception types, messages and stack traces only.
// LinkPeek never puts PATs, URLs or chat text into exception messages.
internal static class CrashLog
{
    private const int MaxEntriesPerSession = 20;
    private const long MaxBytes = 256 * 1024;
    private static readonly object Sync = new();
    private static int entries;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkPeek", "crash.log");

    public static void Write(string kind, Exception? error)
    {
        lock (Sync)
        {
            if (++entries > MaxEntriesPerSession) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} [{kind}] LinkPeek {SettingsWindow.AppVersion} " +
                    $"Windows {Environment.OSVersion.Version}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
