namespace GrandiaModloader;

/// <summary>
/// Product version shown in the app and compared to GitHub release tags.
/// Not the MSI/setup version. Bump this by hand before publishing a release
/// (tag <c>v1.0.1</c> or <c>1.0.1</c> on
/// https://github.com/Wakamu/GrandiaModloader).
/// </summary>
internal static class AppVersion
{
    public const string Current = "1.1.1";

    public const string GitHubOwner = "Wakamu";
    public const string GitHubRepo = "GrandiaModloader";

    public static string Display => $"v{Current}";

    public static string ReleasesUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";

    public static string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
}
