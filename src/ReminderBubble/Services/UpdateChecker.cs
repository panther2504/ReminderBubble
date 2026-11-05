using System.Text.Json;

namespace ReminderBubble.Services;

/// <summary>A newer release that can be installed.</summary>
public sealed record UpdateInfo(string Version, string Url, string FileName, string? Sha256, string Notes, string PageUrl);

/// <summary>
/// Remembers the last answer and its ETag so repeat checks can be conditional. GitHub does not count a
/// "304 Not Modified" against the rate limit, which is what makes checking every minute affordable.
/// </summary>
public sealed class UpdateFeed
{
    public string? ETag { get; set; }
    public UpdateInfo? Cached { get; set; }
    public bool HasCache { get; set; }
}

/// <summary>
/// Looks at the project's latest GitHub release and decides whether it is newer than the running version.
/// Pure logic (no UI, no files) so it can be unit-tested.
/// </summary>
public static class UpdateChecker
{
    public const string Repo = "panther2504/ReminderBubble";
    public static string LatestUrl => $"https://api.github.com/repos/{Repo}/releases/latest";

    /// <summary>1.2 and 1.2.0.0 compare equal (System.Version treats a missing component as "older").</summary>
    public static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>"v1.2.3", "1.2.3-beta" -> 1.2.3. False for anything that is not a version.</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var t = tag.Trim().TrimStart('v', 'V');
        var cut = t.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) t = t[..cut];
        if (!Version.TryParse(t, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    static bool IsTrustedDownload(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return h == "github.com" || h.EndsWith(".github.com") || h.EndsWith(".githubusercontent.com");
    }

    /// <summary>
    /// Reads the GitHub "latest release" JSON. Returns the installer to download, or null when there is nothing newer
    /// (same / older version, draft, pre-release, or no suitable installer attached).
    /// The small online installer is preferred; the "-offline" one is never picked for auto-update.
    /// </summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return null;
        if (Bool(r, "draft") || Bool(r, "prerelease")) return null;
        if (!r.TryGetProperty("tag_name", out var tagEl) || !TryParseVersion(tagEl.GetString(), out var latest)) return null;
        if (latest <= Normalize(current)) return null;
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!name.StartsWith("ReminderBubble-Setup-", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || name.Contains("offline", StringComparison.OrdinalIgnoreCase)) continue;

            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (!IsTrustedDownload(url)) continue;

            string? sha = null;
            if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = digest[7..].Trim().ToLowerInvariant();

            var notes = r.TryGetProperty("body", out var b) ? (b.GetString() ?? "").Trim() : "";
            if (notes.Length > 2000) notes = notes[..2000];
            var page = r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            return new UpdateInfo(latest.ToString(3), url!, name, sha, notes, page);
        }
        return null;
    }

    static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Asks GitHub for the latest release. Throws with a readable message when it cannot be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, Version current, UpdateFeed? feed = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        if (feed is { HasCache: true, ETag: { Length: > 0 } tag }) req.Headers.TryAddWithoutValidation("If-None-Match", tag);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotModified && feed is { HasCache: true }) return feed.Cached;
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("No published release was found (the repository must be public and have a release).");
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub answered {(int)resp.StatusCode}.");
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var info = Parse(json, current);
        if (feed is not null) { feed.ETag = resp.Headers.ETag?.Tag; feed.Cached = info; feed.HasCache = true; }
        return info;
    }
}
