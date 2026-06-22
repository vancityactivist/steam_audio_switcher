using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigPictureAudioSwitcher;

/// <summary>
/// Persisted user configuration. Serialized as JSON to
/// %APPDATA%\BigPictureAudioSwitcher\config.json.
///
/// IMPORTANT: device selections are stored as AudioSwitcher device Id GUIDs
/// (strings), never friendly names — names can duplicate or change across
/// reboots / driver updates, while the Id is stable.
/// </summary>
public sealed class Config
{
    /// <summary>Receiver/AVR device Id. Used while Big Picture is active.</summary>
    public string? ReceiverDeviceId { get; set; }

    /// <summary>Headset device Id. Used while Big Picture is idle (restored to).</summary>
    public string? HeadsetDeviceId { get; set; }

    /// <summary>Poll interval in milliseconds. Default 2s.</summary>
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>When true, detection still runs and updates the status line, but no switching occurs.</summary>
    public bool Paused { get; set; }

    /// <summary>Show a balloon notification on each automatic switch.</summary>
    public bool NotifyOnSwitch { get; set; } = true;

    /// <summary>Reflects whether the HKCU ...\Run entry should exist. Actual state is reconciled on startup.</summary>
    public bool StartWithWindows { get; set; }

    // ---- Persistence plumbing ----

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>%APPDATA%\BigPictureAudioSwitcher</summary>
    public static string AppDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BigPictureAudioSwitcher");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    /// <summary>
    /// Load config from disk, or return defaults if the file is missing/corrupt.
    /// Never throws — a bad config file should not stop the app from starting.
    /// </summary>
    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<Config>(json, JsonOptions);
                if (cfg is not null)
                    return cfg;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load config, using defaults: {ex.Message}");
        }
        return new Config();
    }

    /// <summary>Persist to disk. Never throws.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save config: {ex.Message}");
        }
    }
}
