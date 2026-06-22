using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace BigPictureAudioSwitcher;

/// <summary>Details of an available update.</summary>
public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string DownloadUrl,
    string HtmlUrl,
    string? Notes);

/// <summary>
/// In-app update mechanism tied to GitHub Releases.
///
/// The repo is public, so the "latest release" endpoint needs no auth (just a
/// User-Agent header, which GitHub requires). We compare the latest release tag
/// (vX.Y.Z) against the running assembly version and, if newer, download the
/// release's BigPictureAudioSwitcher.exe asset and self-replace.
///
/// Self-replace on Windows: you cannot overwrite a running .exe, but you CAN
/// rename it. So we rename the running exe to "<exe>.old", move the freshly
/// downloaded exe into the original path, relaunch it, and exit. On next
/// startup we delete any leftover ".old"/".new" files (see CleanupLeftovers).
/// </summary>
public static class Updater
{
    private const string Repo = "vancityactivist/steam_audio_switcher";
    private const string AssetName = "BigPictureAudioSwitcher.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BigPictureAudioSwitcher", CurrentVersion.ToString()));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Running app version, normalized to Major.Minor.Build.</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Normalize(v);
        }
    }

    /// <summary>
    /// Query GitHub for the latest release. Returns an <see cref="UpdateInfo"/>
    /// only if it is strictly newer than the running version AND ships the
    /// expected .exe asset. Returns null otherwise (including on any error).
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{Repo}/releases/latest";
            var json = await Http.GetStringAsync(url).ConfigureAwait(true);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            if (!TryParseTag(tag, out var latest))
            {
                Logger.Log($"Update check: could not parse release tag '{tag}'.");
                return null;
            }

            if (latest <= CurrentVersion)
            {
                Logger.Log($"Update check: up to date (current {CurrentVersion}, latest {latest}).");
                return null;
            }

            // Find the .exe asset's download URL.
            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Logger.Log($"Update check: release {tag} has no '{AssetName}' asset.");
                return null;
            }

            var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

            Logger.Log($"Update available: {latest} (current {CurrentVersion}).");
            return new UpdateInfo(latest, tag!, downloadUrl!, htmlUrl, notes);
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download the update and stage the self-replace. On success the new exe
    /// sits at the original path, the old one is renamed aside, and the new exe
    /// has been launched — the caller should then exit the current process.
    /// Returns false (and leaves the running exe untouched) on any failure.
    /// </summary>
    public static async Task<bool> DownloadAndApplyAsync(UpdateInfo info)
    {
        var current = Environment.ProcessPath ?? Application.ExecutablePath;
        var newPath = current + ".new";
        var oldPath = current + ".old";

        try
        {
            // Download to a sidecar file first so a failed/partial download can
            // never corrupt the running exe.
            if (File.Exists(newPath))
                File.Delete(newPath);

            using (var resp = await Http.GetAsync(info.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(true);
                await using var dst = File.Create(newPath);
                await src.CopyToAsync(dst).ConfigureAwait(true);
            }

            // Swap: rename the running exe aside, then move the new one in.
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(current, oldPath);
            try
            {
                File.Move(newPath, current);
            }
            catch
            {
                // Roll back the rename so the app remains runnable.
                File.Move(oldPath, current);
                throw;
            }

            // Launch the new version and let the caller exit.
            Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
            Logger.Log($"Update applied: now running {info.Version}. Restarting.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Update download/apply failed: {ex.Message}");
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { /* ignore */ }
            return false;
        }
    }

    /// <summary>
    /// Delete leftover ".old"/".new" files from a previous update. Best-effort;
    /// the ".old" file is locked only momentarily after restart, so a delete may
    /// fail on the very first try — that's fine, it'll be cleaned next launch.
    /// </summary>
    public static void CleanupLeftovers()
    {
        try
        {
            var current = Environment.ProcessPath ?? Application.ExecutablePath;
            foreach (var ext in new[] { ".old", ".new" })
            {
                var p = current + ext;
                if (File.Exists(p))
                {
                    try { File.Delete(p); } catch { /* still locked; next time */ }
                }
            }
        }
        catch { /* best-effort */ }
    }

    // ---- helpers ----

    private static Version Normalize(Version v) =>
        new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

    private static bool TryParseTag(string tag, out Version version)
    {
        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        if (Version.TryParse(s, out var v))
        {
            version = Normalize(v);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }
}
