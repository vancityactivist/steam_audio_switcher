using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace BigPictureAudioSwitcher;

/// <summary>
/// Lightweight, immutable description of a playback device for the tray menu.
/// </summary>
public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// Thin wrapper over AudioSwitcher.AudioApi.CoreAudio. AudioSwitcher wraps the
/// undocumented IPolicyConfig COM interface, which is what actually lets us set
/// the default output device (NAudio cannot do this — it can only enumerate).
///
/// Every public method is wrapped so a transiently-missing or unplugged device
/// can never bubble an exception into the polling loop.
/// </summary>
public sealed class AudioManager
{
    private readonly CoreAudioController _controller = new();

    /// <summary>
    /// Enumerate ACTIVE playback devices. Returns an empty list on failure.
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices()
    {
        try
        {
            return _controller
                .GetPlaybackDevices(DeviceState.Active)
                .Select(d => new AudioDeviceInfo(d.Id.ToString(), d.FullName))
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to enumerate playback devices: {ex.Message}");
            return Array.Empty<AudioDeviceInfo>();
        }
    }

    /// <summary>
    /// The Id of the current default (multimedia) playback device, or null.
    /// </summary>
    public string? GetCurrentDefaultId()
    {
        try
        {
            return _controller.DefaultPlaybackDevice?.Id.ToString();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to read default playback device: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Friendly name for a device Id, or null if not found / errored.
    /// </summary>
    public string? GetDeviceName(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;
        try
        {
            if (Guid.TryParse(deviceId, out var guid))
            {
                var dev = _controller.GetDevice(guid);
                return dev?.FullName;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to resolve device name for {deviceId}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Set the given device as default for ALL roles (Console/Multimedia +
    /// Communications) so everything moves together. Returns true on success.
    /// </summary>
    public bool SetDefault(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            Logger.Log("SetDefault called with no device Id; ignoring.");
            return false;
        }

        try
        {
            if (!Guid.TryParse(deviceId, out var guid))
            {
                Logger.Log($"SetDefault: '{deviceId}' is not a valid GUID.");
                return false;
            }

            var device = _controller.GetDevice(guid);
            if (device is null)
            {
                Logger.Log($"SetDefault: device {deviceId} not found (unplugged?).");
                return false;
            }
            if (!device.IsPlaybackDevice || device.State != DeviceState.Active)
            {
                Logger.Log($"SetDefault: device {device.FullName} is not an active playback device.");
                return false;
            }

            // Move the default for all roles together.
            device.SetAsDefault();
            device.SetAsDefaultCommunications();

            Logger.Log($"Switched default output to: {device.FullName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to set default device {deviceId}: {ex.Message}");
            return false;
        }
    }
}
