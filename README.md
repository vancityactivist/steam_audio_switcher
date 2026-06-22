# Big Picture Audio Switcher

A tiny Windows system-tray app (C# / .NET 8 / WinForms) that **automatically
switches your default audio output device when Steam Big Picture mode opens, and
switches it back when it closes.**

Typical use: route audio to your **AVR / receiver** (TV + speakers) when you
drop into Big Picture on the couch, and back to your **headset** when you exit.

- Single self-contained `.exe`, no external CLI tools.
- No main window — lives entirely in the system tray.
- No admin rights required for normal operation.
- Only one third-party dependency: `AudioSwitcher.AudioApi.CoreAudio`.

---

## How detection works (and why it's not just "is Steam running")

Big Picture runs as `steamwebhelper.exe` — **the same process** as the ordinary
desktop Steam client. So process presence alone tells you nothing.

Instead this app discriminates by **window geometry**: it enumerates the
top-level windows owned by `steamwebhelper.exe` and checks whether any of them
is sized to **exactly fill a monitor** (within ~4 px) and is large enough
(> 1000×600) to not be a transient popup. The desktop client, even maximized,
never matches a monitor's bounds exactly, so this cleanly separates the two.

Polling happens on a WinForms timer every 2 s (configurable in `config.json`),
and the audio switch only fires on **transitions** (idle→active, active→idle),
never on every poll.

---

## Build & publish

> ⚠️ This must be **built/published on Windows.** WinForms cannot reliably
> cross-compile from macOS. Edit on whatever you like; publish on a Windows box
> (or a Windows VM / CI runner) with the .NET 8 SDK installed.

From the project directory on Windows:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

(The `.csproj` already sets these properties, so a plain
`dotnet publish -c Release` works too — the command above is just explicit.)

The single self-contained exe lands in:

```
bin\Release\net8.0-windows\win-x64\publish\BigPictureAudioSwitcher.exe
```

Copy that one file anywhere and run it. No .NET install needed on the target.

---

## First-run setup (picking your devices)

1. Run `BigPictureAudioSwitcher.exe`. An icon appears in the system tray.
2. Right-click the tray icon.
3. Under **"Receiver (AVR) device"**, click your receiver/TV output. A checkmark
   marks the current selection.
4. Under **"Headset device"**, click your headset output.

That's it. Selections are saved immediately to
`%APPDATA%\BigPictureAudioSwitcher\config.json` as device **Id GUIDs** (not
names — names can duplicate or change), and re-resolved on every launch. If a
saved device is later unplugged, the menu shows *"(saved device not found)"*
instead of breaking.

### Other tray options

- **Default: …** / **Big Picture: …** — live status lines (refresh when you open the menu / via the tray tooltip).
- **Switch to receiver now** / **Switch to headset now** — manual one-off switch.
- **Pause auto-switching** — detection keeps updating the status, but no switching happens.
- **Notify on switch** — show a balloon notification on each auto-switch.
- **Start with Windows** — adds/removes a per-user (HKCU) `Run` entry pointing at the exe. Reflects the real registry state on startup.
- **Quit**.

---

## Testing detection

1. Run the exe and set your two devices (above).
2. Open Steam **Big Picture mode**. Within ~2 s the default output should switch
   to your receiver; you'll get a balloon notification (if enabled). Hover the
   tray icon — the tooltip shows `Big Picture: active`.
3. Exit Big Picture. Within ~2 s it should switch back to your headset, and the
   tooltip should read `Big Picture: idle`.
4. Watch the log (below) to see transitions and switches in real time.

---

## Logging

For the first few days, a timestamped log is written to:

```
%APPDATA%\BigPictureAudioSwitcher\log.txt
```

It records startup/shutdown, detected transitions, switches, and any
device/enumeration errors (which are caught so they can never crash the polling
loop). The file self-truncates past ~1 MB. Safe to delete any time.

---

## Configuration file

`%APPDATA%\BigPictureAudioSwitcher\config.json`:

```json
{
  "ReceiverDeviceId": "....",
  "HeadsetDeviceId": "....",
  "PollIntervalMs": 2000,
  "Paused": false,
  "NotifyOnSwitch": true,
  "StartWithWindows": false
}
```

Edit `PollIntervalMs` to change how often detection runs (the app clamps to a
500 ms minimum). Everything else is controlled from the tray menu.

---

## Customising the icon

`app.ico` is an embedded placeholder. Replace it with your own
16×16 / 32×32 `.ico` and rebuild — it's wired up via `<ApplicationIcon>` and an
`<EmbeddedResource>` in `BigPictureAudioSwitcher.csproj`, and loaded by
`TrayApplicationContext.LoadIcon()`.

---

## Project layout

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point; single-instance guard; runs the tray context. |
| `TrayApplicationContext.cs` | The app: tray icon, poll timer, menu, transition logic. |
| `BigPictureDetector.cs` | P/Invoke window-geometry detection of Big Picture. |
| `AudioManager.cs` | AudioSwitcher wrapper: enumerate + set default output. |
| `Config.cs` | JSON config in `%APPDATA%`. |
| `StartupManager.cs` | HKCU `Run` key for "Start with Windows". |
| `Logger.cs` | Best-effort timestamped log file. |

---

## Why AudioSwitcher and not NAudio?

NAudio can *enumerate* devices but **cannot set the default output device**.
`AudioSwitcher.AudioApi.CoreAudio` wraps the undocumented `IPolicyConfig` COM
interface, which is the correct (and only practical) way to change the default
from user code. We call both `SetAsDefault()` and `SetAsDefaultCommunications()`
so every audio role moves together.
