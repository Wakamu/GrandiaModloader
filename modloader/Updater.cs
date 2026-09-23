using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace GrandiaModloader;

internal static class Updater
{
    private static readonly HttpClient Http = CreateClient();

    public static string UpdateRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrandiaModloader", "update");

    public static async Task DownloadAsync(string url, string destFile, IProgress<int>? progress,
        CancellationToken cancel = default)
    {
        if (!IsTrustedDownload(url))
        {
            throw new InvalidOperationException("Update URL is not a GitHub release asset.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancel);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var input = await resp.Content.ReadAsStreamAsync(cancel);
        await using var output = File.Create(destFile);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await input.ReadAsync(buffer, cancel)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), cancel);
            read += n;
            if (total > 0)
            {
                progress?.Report((int)Math.Clamp(read * 100 / total, 0, 100));
            }
        }

        progress?.Report(100);
    }

    public static void ExtractZip(string zipFile, string destDir)
    {
        if (Directory.Exists(destDir))
        {
            Directory.Delete(destDir, recursive: true);
        }

        Directory.CreateDirectory(destDir);
        var root = Path.GetFullPath(destDir);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        using var zip = ZipFile.OpenRead(zipFile);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                var dir = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Update zip has an unsafe path.");
                }

                Directory.CreateDirectory(dir);
                continue;
            }

            var dest = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Update zip has an unsafe path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    public static bool LooksLikePayload(string extractDir) =>
        File.Exists(Path.Combine(extractDir, "GrandiaModloader.exe")) &&
        File.Exists(Path.Combine(extractDir, "GrandiaMod.dll"));

    public static bool CanWriteInstallDir(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".update-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts a helper that waits for this process to exit, copies
    /// <paramref name="extractDir"/> over the install folder, then relaunches.
    /// Returns false if UAC was cancelled.
    /// </summary>
    public static bool StartApply(string extractDir, string installDir)
    {
        var script = Path.Combine(UpdateRoot, "apply.ps1");
        Directory.CreateDirectory(UpdateRoot);
        File.WriteAllText(script, ApplyScript);
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -TargetPid {Environment.ProcessId} -Source \"{extractDir}\" -Dest \"{installDir}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (!CanWriteInstallDir(installDir))
        {
            psi.Verb = "runas";
        }

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool IsTrustedDownload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GrandiaModloader", AppVersion.Current));
        return http;
    }

    private const string ApplyScript =
        """
        param(
          [Parameter(Mandatory = $true)][int]$TargetPid,
          [Parameter(Mandatory = $true)][string]$Source,
          [Parameter(Mandatory = $true)][string]$Dest
        )
        $ErrorActionPreference = 'Stop'
        while (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue) {
          Start-Sleep -Seconds 1
        }
        if (-not (Test-Path -LiteralPath (Join-Path $Source 'GrandiaModloader.exe'))) {
          exit 2
        }
        New-Item -ItemType Directory -Force -Path $Dest | Out-Null
        $args = @(
          $Source, $Dest, '/E', '/IS', '/IT',
          '/NFL', '/NDL', '/NJH', '/NJS', '/NC', '/NS', '/NP',
          '/XD', 'mods', 'overlay', 'update',
          '/XF', 'config.json', '*.pdb'
        )
        & robocopy @args
        if ($LASTEXITCODE -ge 8) { exit $LASTEXITCODE }
        $exe = Join-Path $Dest 'GrandiaModloader.exe'
        Start-Process -FilePath $exe
        """;
}
