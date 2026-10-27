using System.Globalization;
using System.Text.RegularExpressions;

namespace LinkPeek.Core;

public sealed record WebLink(string Text, Uri Uri)
{
    public static WebLink? Parse(string? text, bool accessibilityHyperlink = false)
    {
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace)) return null;
        if (accessibilityHyperlink && text.StartsWith("dev.azure.com/", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            uri.Host.Length == 0 || uri.UserInfo.Length != 0 || text.Contains('\\')) return null;
        return new(text, uri);
    }

    public PullRequestLink? PullRequest => PullRequestLink.Parse(this);
}

public sealed record PullRequestLink(string Organization, string Project, string Repository, string Id)
{
    public static bool ValidOrganization(string value) =>
        Regex.IsMatch(value, "\\A[A-Za-z0-9-]{1,100}\\z", RegexOptions.CultureInvariant);

    public static PullRequestLink? Parse(WebLink link)
    {
        if (link.Uri.Scheme != "https" || link.Uri.Host != "dev.azure.com" || link.Uri.Port != 443)
            return null;
        // Inspect the original path, before Uri canonicalizes dot segments.
        var match = Regex.Match(link.Text,
            @"\Ahttps://dev\.azure\.com(?::443)?/([^/?#]+)/([^/?#]+)/_git/([^/?#]+)/pullrequest/([0-9]+)(?:[?#].*)?\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !ulong.TryParse(match.Groups[4].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number) || number == 0) return null;
        var segments = Enumerable.Range(1, 3).Select(i => Uri.UnescapeDataString(match.Groups[i].Value)).ToArray();
        if (!ValidOrganization(segments[0]) || segments.Any(s => s is "." or ".." ||
                s.Contains('/') || s.Contains('\\') || s.Any(char.IsControl))) return null;
        return new(segments[0].ToLowerInvariant(), segments[1], segments[2], match.Groups[4].Value);
    }
}

public sealed class DwellGate
{
    public string? Key { get; private set; }
    public long Generation { get; private set; }
    private TimeSpan entered;
    private bool started;

    public void Observe(string? key, TimeSpan now)
    {
        if (key == Key) return;
        Key = key;
        entered = now;
        started = false;
        Generation++;
    }

    public bool Start(TimeSpan now, TimeSpan delay)
    {
        if (Key is null || started || now - entered < delay) return false;
        started = true;
        return true;
    }

    public void Reset() { Key = null; started = false; Generation++; }
}
