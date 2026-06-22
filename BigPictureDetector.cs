using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BigPictureAudioSwitcher;

/// <summary>
/// Detects whether Steam Big Picture mode is currently on screen.
///
/// Why this is non-trivial: Big Picture runs inside steamwebhelper.exe — the
/// SAME process as the ordinary desktop Steam client. So "is steamwebhelper
/// running?" is NOT a usable signal; it's basically always running when Steam
/// is open.
///
/// The discriminator is window geometry. Big Picture presents a borderless
/// window sized to exactly fill a monitor. The desktop client, even maximized,
/// never matches a monitor's bounds exactly (it leaves the taskbar / has a
/// frame). So: enumerate top-level windows owned by steamwebhelper.exe, get
/// each window's rect, and if any one matches a monitor's bounds within a few
/// pixels AND is large enough to not be a transient popup, we call it active.
/// </summary>
public static class BigPictureDetector
{
    // Pixel tolerance when comparing a window rect to a monitor's bounds.
    private const int Tolerance = 4;

    // Minimum size to be considered Big Picture (filters small/transient windows).
    private const int MinWidth = 1000;
    private const int MinHeight = 600;

    private const string SteamWebHelper = "steamwebhelper";

    /// <summary>
    /// Returns true if a Big Picture window is currently displayed.
    /// Best-effort: any failure is logged and treated as "not active" so the
    /// polling loop can never be crashed by a transient Win32 error.
    /// </summary>
    public static bool IsBigPictureActive()
    {
        try
        {
            // Collect process IDs for every steamwebhelper.exe instance.
            var steamPids = new HashSet<uint>();
            foreach (var proc in Process.GetProcessesByName(SteamWebHelper))
            {
                steamPids.Add((uint)proc.Id);
                proc.Dispose();
            }
            if (steamPids.Count == 0)
                return false; // Steam not running -> definitely not Big Picture.

            // Snapshot monitor bounds once.
            var monitors = Screen.AllScreens
                .Select(s => s.Bounds)
                .ToArray();

            bool found = false;

            EnumWindows((hWnd, _) =>
            {
                // Skip windows that aren't visible on screen.
                if (!IsWindowVisible(hWnd))
                    return true; // continue enumeration

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (!steamPids.Contains(pid))
                    return true;

                if (!GetWindowRect(hWnd, out RECT r))
                    return true;

                int width = r.Right - r.Left;
                int height = r.Bottom - r.Top;

                if (width < MinWidth || height < MinHeight)
                    return true;

                // Does this window match any monitor's bounds within tolerance?
                foreach (var m in monitors)
                {
                    if (Math.Abs(width - m.Width) <= Tolerance &&
                        Math.Abs(height - m.Height) <= Tolerance)
                    {
                        found = true;
                        return false; // stop enumerating; we have our answer.
                    }
                }

                return true; // keep looking
            }, IntPtr.Zero);

            return found;
        }
        catch (Exception ex)
        {
            Logger.Log($"Big Picture detection error (treating as idle): {ex.Message}");
            return false;
        }
    }

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
