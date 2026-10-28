namespace LinkPeek.Core;

/// <summary>Hover dwell bounds and the one-time move from the 0.1.x defaults (500 ms, 300-1000 ms).</summary>
public static class HoverDelay
{
    public const int Minimum = 500;
    public const int Maximum = 1500;
    public const int Default = 800;
    public const int CurrentVersion = 2;

    /// <summary>Version 0 (unversioned) stored 500 ms for users who never touched the slider.</summary>
    public static int Resolve(int stored, int version)
    {
        if (stored == 0 || (version < CurrentVersion && stored == 500)) return Default;
        return Math.Clamp(stored, Minimum, Maximum);
    }
}

/// <summary>
/// Case-insensitive host/path glob. * matches any run of characters; a pattern without a slash
/// matches the host and every path on it. Scheme, query, fragment and port are ignored.
/// </summary>
public static class LinkPattern
{
    public static string? Normalize(string? raw)
    {
        var text = (raw ?? "").Trim().ToLowerInvariant();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 3)..];
        text = text.TrimEnd('/');
        if (text.Length == 0 || text.Any(char.IsWhiteSpace) || text.All(c => c is '*' or '/')) return null;
        return text.Contains('/') ? text : text + "/*";
    }

    public static bool Matches(string pattern, Uri uri)
    {
        if (Normalize(pattern) is not { } normalized || uri.Host.Length == 0) return false;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).ToLowerInvariant().TrimEnd('/');
        return Glob(normalized, uri.Host.ToLowerInvariant() + (path.Length == 0 ? "/" : path));
    }

    private static bool Glob(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*') { star = p; resume = t; p++; }
            else if (p < pattern.Length && pattern[p] == text[t]) { p++; t++; }
            else if (star >= 0) { resume++; t = resume; p = star + 1; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}

/// <summary>Decides which non-pull-request links may open a QR card.</summary>
public sealed record LinkFilter(
    bool QrEnabled = true, bool GrabEnabled = true,
    IReadOnlyList<string>? Patterns = null, bool AnyOtherLink = false)
{
    public const string GrabPattern = "app.grab.com/s/*";

    public bool AllowsQr(Uri uri)
    {
        if (!QrEnabled) return false;
        if (AnyOtherLink) return true;
        if (GrabEnabled && LinkPattern.Matches(GrabPattern, uri)) return true;
        return (Patterns ?? []).Any(pattern => LinkPattern.Matches(pattern, uri));
    }
}
