namespace MovieDB.Common;

/// <summary>Guards redirects against open-redirect attacks: only a relative path on this host is local.</summary>
public static class LocalUrl
{
    /// <summary>True for a path starting with a single '/', without '//' or '/\' prefixes or control characters.</summary>
    /// <param name="url">The URL to check.</param>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        {
            return false;
        }

        return url.All(c => !char.IsControl(c) && c != '\\');
    }

    public static string OrDefault(string? url, string fallback = "/") => IsLocal(url) ? url! : fallback;
}
