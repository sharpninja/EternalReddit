namespace EternalReddit.Server;

/// <summary>
/// Rewrites the SPA's root-anchored assets when the app is mounted under a path
/// prefix behind the EternalSocial proxy (e.g. PATH_BASE=/r): the index.html base
/// href and the PWA manifest's start_url/scope. The manifest always flows through
/// the /app.webmanifest endpoint so the rewrite applies uniformly.
/// </summary>
public static class PathBaseAssets
{
    public static string RewriteIndex(string html, string pathBase)
    {
        var prefix = Normalize(pathBase);
        return html
            .Replace("<base href=\"/\" />", $"<base href=\"{prefix}/\" />")
            .Replace("href=\"manifest.webmanifest\"", "href=\"app.webmanifest\"");
    }

    public static string RewriteManifest(string json, string pathBase)
    {
        var prefix = Normalize(pathBase);
        return json
            .Replace("\"start_url\": \"/\"", $"\"start_url\": \"{prefix}/\"")
            .Replace("\"scope\": \"/\"", $"\"scope\": \"{prefix}/\"");
    }

    private static string Normalize(string pathBase)
        => string.IsNullOrWhiteSpace(pathBase) ? "" : "/" + pathBase.Trim('/');
}
