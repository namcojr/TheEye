using System.IO;

namespace TheEye.Desktop.Services;

/// <summary>
/// The crash log the Windows shell writes, in the same per-user folder: the
/// settings file and the log sit together under TheEyeIVAO on every platform.
/// </summary>
internal static class CrashLog
{
    public static void Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TheEyeIVAO");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never take the application down.
        }
    }
}
