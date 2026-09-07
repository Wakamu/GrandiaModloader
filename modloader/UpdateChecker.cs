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
    string? Error = null)
{
    public bool IsNewer => Status == UpdateCheckStatus.Available && !string.IsNullOrEmpty(Remote);
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

            var remote = Normalize(tag);
            if (IsNewer(remote, current))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Available, current, remote, url);
            }

            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, current, remote, url);
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
