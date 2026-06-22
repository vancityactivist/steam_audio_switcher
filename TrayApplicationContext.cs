using System.Reflection;

namespace BigPictureAudioSwitcher;

/// <summary>
/// The application. Owns the tray icon, the poll timer, the audio manager and
/// the config. Has no window. Created and run by <see cref="Program"/>.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AudioManager _audio = new();
    private readonly Config _config;

    // Menu items we need to mutate on each refresh.
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _defaultDeviceItem;
    private readonly ToolStripMenuItem _defaultDeviceMenu;
    private readonly ToolStripMenuItem _bigPictureDeviceMenu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _notifyItem;
    private readonly ToolStripMenuItem _startupItem;

    /// <summary>Tracks the last detected Big Picture state so we only act on transitions.</summary>
    private bool _bigPictureActive;

    /// <summary>True once we've seen at least one poll, so the first poll establishes a baseline without switching.</summary>
    private bool _initialized;

    public TrayApplicationContext()
    {
        _config = Config.Load();

        // Reconcile the persisted "start with Windows" preference with the
        // actual registry state — the registry is the source of truth.
        _config.StartWithWindows = StartupManager.IsEnabled();

        // ---- Build the menu skeleton (contents filled in by RefreshMenu) ----
        _menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("Big Picture: …") { Enabled = false };
        _defaultDeviceItem = new ToolStripMenuItem("Default: …") { Enabled = false };

        _defaultDeviceMenu = new ToolStripMenuItem("Default audio device");
        _bigPictureDeviceMenu = new ToolStripMenuItem("Steam Big Picture audio device");

        var switchToDefault = new ToolStripMenuItem("Switch to default device now", null,
            (_, _) => ManualSwitch(_config.DefaultDeviceId, "default"));
        var switchToBigPicture = new ToolStripMenuItem("Switch to Big Picture device now", null,
            (_, _) => ManualSwitch(_config.BigPictureDeviceId, "Big Picture"));

        _pauseItem = new ToolStripMenuItem("Pause auto-switching", null, OnTogglePause)
        {
            CheckOnClick = true,
            Checked = _config.Paused,
        };

        _notifyItem = new ToolStripMenuItem("Notify on switch", null, OnToggleNotify)
        {
            CheckOnClick = true,
            Checked = _config.NotifyOnSwitch,
        };

        _startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup)
        {
            CheckOnClick = true,
            Checked = _config.StartWithWindows,
        };

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => ExitApp());

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            _defaultDeviceItem,
            new ToolStripSeparator(),
            _defaultDeviceMenu,
            _bigPictureDeviceMenu,
            new ToolStripSeparator(),
            switchToDefault,
            switchToBigPicture,
            new ToolStripSeparator(),
            _pauseItem,
            _notifyItem,
            _startupItem,
            new ToolStripSeparator(),
            quitItem,
        });

        // Rebuild the device submenus + status every time the menu opens so it
        // always reflects freshly enumerated devices.
        _menu.Opening += (_, _) => RefreshMenu();

        // ---- Tray icon ----
        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Big Picture Audio Switcher",
            Visible = true,
            ContextMenuStrip = _menu,
        };

        // ---- Poll timer ----
        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(500, _config.PollIntervalMs),
        };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        // Do an immediate first poll to establish a baseline.
        Poll();
    }

    // ----------------------------------------------------------------------
    // Polling / transition logic
    // ----------------------------------------------------------------------

    private void Poll()
    {
        bool active = BigPictureDetector.IsBigPictureActive();

        // Update tray tooltip with current state regardless of pause.
        var currentName = _audio.GetDeviceName(_audio.GetCurrentDefaultId()) ?? "unknown";
        _trayIcon.Text = Truncate(
            $"Big Picture: {(active ? "active" : "idle")}\nDefault: {currentName}", 63);

        if (!_initialized)
        {
            // First poll just establishes the baseline; don't switch on startup.
            _bigPictureActive = active;
            _initialized = true;
            return;
        }

        if (active == _bigPictureActive)
            return; // No transition — nothing to do.

        // Transition detected.
        _bigPictureActive = active;
        Logger.Log($"Transition: Big Picture is now {(active ? "ACTIVE" : "IDLE")}.");

        if (_config.Paused)
        {
            Logger.Log("Auto-switching is paused; not switching.");
            return;
        }

        if (active)
            AutoSwitch(_config.BigPictureDeviceId, "Big Picture audio device (Big Picture opened)");
        else
            AutoSwitch(_config.DefaultDeviceId, "default audio device (Big Picture closed)");
    }

    private void AutoSwitch(string? deviceId, string label)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            Logger.Log($"No device configured for {label}; skipping switch. " +
                       "Pick one from the tray menu.");
            return;
        }

        bool ok = _audio.SetDefault(deviceId);
        if (ok && _config.NotifyOnSwitch)
        {
            var name = _audio.GetDeviceName(deviceId) ?? "device";
            _trayIcon.ShowBalloonTip(2000, "Audio switched",
                $"Now using: {name}", ToolTipIcon.Info);
        }
    }

    private void ManualSwitch(string? deviceId, string label)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            _trayIcon.ShowBalloonTip(3000, "No device set",
                $"Pick a {label} device from the tray menu first.", ToolTipIcon.Warning);
            return;
        }
        bool ok = _audio.SetDefault(deviceId);
        if (ok && _config.NotifyOnSwitch)
        {
            var name = _audio.GetDeviceName(deviceId) ?? "device";
            _trayIcon.ShowBalloonTip(2000, "Audio switched",
                $"Now using: {name}", ToolTipIcon.Info);
        }
    }

    // ----------------------------------------------------------------------
    // Menu refresh + handlers
    // ----------------------------------------------------------------------

    private void RefreshMenu()
    {
        var devices = _audio.GetPlaybackDevices();
        var currentDefaultId = _audio.GetCurrentDefaultId();

        _statusItem.Text = $"Big Picture: {(_bigPictureActive ? "active" : "idle")}";

        var defaultName = _audio.GetDeviceName(currentDefaultId) ?? "unknown";
        _defaultDeviceItem.Text = $"Default: {defaultName}";

        BuildDeviceSubmenu(_defaultDeviceMenu, devices, _config.DefaultDeviceId,
            id => { _config.DefaultDeviceId = id; _config.Save(); });
        BuildDeviceSubmenu(_bigPictureDeviceMenu, devices, _config.BigPictureDeviceId,
            id => { _config.BigPictureDeviceId = id; _config.Save(); });
    }

    /// <summary>
    /// Rebuild a device submenu. Each active device is a clickable item; the
    /// currently-selected one gets a checkmark. If the saved selection is no
    /// longer present (unplugged etc.) we show a disabled "(saved device not
    /// found)" hint instead of crashing.
    /// </summary>
    private static void BuildDeviceSubmenu(
        ToolStripMenuItem parent,
        IReadOnlyList<AudioDeviceInfo> devices,
        string? selectedId,
        Action<string> onSelect)
    {
        parent.DropDownItems.Clear();

        if (devices.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("(no playback devices found)")
            {
                Enabled = false,
            });
            return;
        }

        bool selectedFound = false;
        foreach (var dev in devices)
        {
            bool isSelected = dev.Id == selectedId;
            if (isSelected) selectedFound = true;

            var item = new ToolStripMenuItem(dev.Name)
            {
                Checked = isSelected,
                CheckOnClick = false,
            };
            var capturedId = dev.Id;
            item.Click += (_, _) => onSelect(capturedId);
            parent.DropDownItems.Add(item);
        }

        // The saved device exists in config but isn't currently present.
        if (!string.IsNullOrEmpty(selectedId) && !selectedFound)
        {
            parent.DropDownItems.Add(new ToolStripSeparator());
            parent.DropDownItems.Add(new ToolStripMenuItem("(saved device not found)")
            {
                Enabled = false,
            });
        }
    }

    private void OnTogglePause(object? sender, EventArgs e)
    {
        _config.Paused = _pauseItem.Checked;
        _config.Save();
        Logger.Log($"Auto-switching {(_config.Paused ? "paused" : "resumed")}.");
    }

    private void OnToggleNotify(object? sender, EventArgs e)
    {
        _config.NotifyOnSwitch = _notifyItem.Checked;
        _config.Save();
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        bool actual = StartupManager.SetEnabled(_startupItem.Checked);
        // Reconcile the checkbox with the real registry result.
        _startupItem.Checked = actual;
        _config.StartWithWindows = actual;
        _config.Save();
    }

    // ----------------------------------------------------------------------
    // Lifecycle / helpers
    // ----------------------------------------------------------------------

    private void ExitApp()
    {
        _timer.Stop();
        _trayIcon.Visible = false;   // Remove from tray immediately.
        _trayIcon.Dispose();
        ExitThread();                // Ends Application.Run.
    }

    /// <summary>
    /// Load the embedded app.ico. Falls back to the system application icon if
    /// the embedded resource can't be found, so the tray always has an icon.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            // EmbeddedResource name is "<RootNamespace>.<file>".
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (resName is not null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream is not null)
                    return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load embedded icon: {ex.Message}");
        }
        return SystemIcons.Application;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _menu.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
