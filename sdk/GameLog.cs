namespace Grandia.Sdk;

/// <summary>
/// Writes lines to <c>GrandiaMod.log</c> next to the game exe (same file the
/// host uses). Each Launch/inject truncates the file first.
/// </summary>
public sealed class GameLog
{
    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    /// <summary>Same as <see cref="Info"/>.</summary>
    public void Write(string message) => Info(message);

    internal static void Write(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var path = LogPath();
            if (path is null)
            {
                return;
            }

            File.AppendAllText(path, $"[GrandiaMod][{level}] {message}\n");
        }
        catch
        {
            // Logging must not throw into a hook.
        }
    }

    private static string? LogPath()
    {
        var exe = Environment.ProcessPath;
        var dir = string.IsNullOrEmpty(exe) ? AppContext.BaseDirectory : Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        return Path.Combine(dir, "GrandiaMod.log");
    }
}
