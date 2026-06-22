namespace BigPictureAudioSwitcher;

/// <summary>
/// Dead-simple timestamped logger to
/// %APPDATA%\BigPictureAudioSwitcher\log.txt.
///
/// Useful for the first few days of use to confirm detection/switching is
/// behaving. It is intentionally best-effort: logging must never throw and
/// must never take down the polling loop. The file is capped so it cannot
/// grow without bound.
/// </summary>
public static class Logger
{
    private const long MaxBytes = 1_000_000; // ~1 MB; truncate beyond this.
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(Config.AppDataDir, "log.txt");

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Config.AppDataDir);

                // Cheap rotation: if the file is too big, start fresh.
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    File.Delete(LogPath);

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Swallow — logging is best-effort and must never crash the app.
        }
    }
}
