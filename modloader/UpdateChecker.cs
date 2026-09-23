using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GrandiaModloader;

internal enum UpdateCheckStatus
{
    UpToDate,
    Available,
    NoReleases,
    Error,
}

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string Current,
    string? Remote = null,
    string? Url = null,
    string? ZipUrl = null,
    string? Error = null)
{
    public bool IsNewer => Status == UpdateCheckStatus.Available && !string.IsNullOrEmpty(Remote);

    public bool CanApply => !string.IsNullOrWhiteSpace(ZipUrl);
}

internal static class UpdateChecker
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancel = default)
    {
        var current = AppVersion.Current;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppVersion.LatestReleaseApiUrl);
            using var resp = await Http.SendAsync(req, cancel);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NoReleases, current, Url: AppVersion.ReleasesUrl);
            }

            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancel);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateCheckResult(UpdateCheckStatus.NoReleases, current, Url: AppVersion.ReleasesUrl);
            }

            var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = AppVersion.ReleasesUrl;
            }

            var zipUrl = FindZipAsset(root);
            var remote = Normalize(tag);
            if (IsNewer(remote, current))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Available, current, remote, url, zipUrl);
            }

            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, current, remote, url, zipUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, current, Url: AppVersion.ReleasesUrl, Error: ex.Message);
        }
    }

    public static string Normalize(string tag)
    {
        var t = tag.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            t = t[1..];
        }

        return t;
    }

    public static bool IsNewer(string remote, string current)
    {
        var a = Normalize(remote);
        var b = Normalize(current);
        if (TryParse(a, out var va) && TryParse(b, out var vb))
        {
            return va > vb;
        }

        return !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindZipAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var href = asset.TryGetProperty("browser_download_url", out var hrefEl)
                ? hrefEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (name.Equals(AppVersion.UpdateZipAsset, StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }

            if (fallback is null &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("GrandiaModloader", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                fallback = href;
            }
        }

        return fallback;
    }

    private static bool TryParse(string text, out Version version)
    {
        if (Version.TryParse(text, out version!))
        {
            return true;
        }

        return Version.TryParse(text + ".0", out version!);
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GrandiaModloader", AppVersion.Current));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}
