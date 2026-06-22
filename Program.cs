namespace BigPictureAudioSwitcher;

/// <summary>
/// Entry point. There is NO main window — the whole app lives in the tray via
/// a custom ApplicationContext (see <see cref="TrayApplicationContext"/>).
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Standard WinForms init.
        ApplicationConfiguration.Initialize();

        // Single-instance guard: if another copy is already running, just exit.
        // Avoids two trays fighting over the default device.
        using var mutex = new Mutex(initiallyOwned: true,
            "BigPictureAudioSwitcher_SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        Logger.Log("=== BigPictureAudioSwitcher starting ===");

        // Run the tray context. No Form is shown; Application.Run keeps the
        // message loop alive until the context calls ExitThread().
        Application.Run(new TrayApplicationContext());

        Logger.Log("=== BigPictureAudioSwitcher exiting ===");
    }
}
