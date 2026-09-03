using System.Diagnostics;

namespace Grandia.Runtime;

internal static class FieldTools
{
    public static string RequireExe(ModsConfig cfg)
    {
        foreach (var candidate in Candidates(cfg))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "field_tools.exe not found. Rebuild GrandiaModloader so field_tools\\field_tools.exe sits next to the exe.");
    }

    public static string Run(ModsConfig cfg, string arguments, string failLabel)
    {
        var exe = RequireExe(cfg);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start field_tools.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail) ? $"{failLabel} exited {proc.ExitCode}" : detail.Trim());
        }

        return stdout;
    }

    public static string Quote(string value)
    {
        if (value.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static IEnumerable<string> Candidates(ModsConfig cfg)
    {
        foreach (var root in new[] { cfg.Tools, cfg.Grandipelago })
        {
            var path = (root ?? "").Trim();
            if (path.Length == 0)
            {
                continue;
            }

            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }

            yield return Path.Combine(path, "field_tools.exe");
            yield return Path.Combine(path, "field_tools", "field_tools.exe");
        }
    }
}
