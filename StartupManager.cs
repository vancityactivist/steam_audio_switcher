using Microsoft.Win32;

namespace BigPictureAudioSwitcher;

/// <summary>
/// Manages the "Start with Windows" behaviour via the per-user Run key:
///   HKCU\Software\Microsoft\Windows\CurrentVersion\Run
///
/// Per-user (HKCU) means NO admin rights are required. The value points at the
/// current exe path, quoted to survive spaces.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BigPictureAudioSwitcher";

    /// <summary>Full path to the running executable.</summary>
    private static string ExePath => Environment.ProcessPath
                                     ?? Application.ExecutablePath;

    /// <summary>True if the Run entry exists and points at this exe.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to read startup state: {ex.Message}");
            return false;
        }
    }

    /// <summary>Create or remove the Run entry. Returns the resulting state.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
                return IsEnabled();

            if (enabled)
                key.SetValue(ValueName, $"\"{ExePath}\"");
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            Logger.Log($"Start with Windows set to {enabled}.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to set startup state: {ex.Message}");
        }
        return IsEnabled();
    }
}
