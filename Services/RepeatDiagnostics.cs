using System.Diagnostics;
using System.IO;

namespace YMusicMini.Services;

internal static class RepeatDiagnostics
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YMusicMini", "repeat.log");

    // No disk logging in normal use. For temporary troubleshooting set the
    // YMUSICMINI_REPEAT_LOG environment variable to 1 before starting the app.
    // Only states and timings are written; no titles, tokens or account data.
    private static bool DiskLoggingEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("YMUSICMINI_REPEAT_LOG"),
            "1", StringComparison.Ordinal);

    public static void Write(string message)
    {
        Debug.WriteLine($"[Repeat] {message}");
        if (!DiskLoggingEnabled) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 128_000)
                File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
