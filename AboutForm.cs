using System.Diagnostics;

namespace BigPictureAudioSwitcher;

/// <summary>
/// Small modal "About" dialog. Built in code (no designer) to keep the app
/// form-free everywhere else. Shows the version and clickable links to the
/// project page and the author's Buy Me a Coffee page.
/// </summary>
public sealed class AboutForm : Form
{
    private const string ProjectUrl = "https://github.com/vancityactivist/steam_audio_switcher";
    private const string CoffeeUrl = "https://buymeacoffee.com/vancityactivist";

    public AboutForm()
    {
        Text = "About — Big Picture Audio Switcher";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 210);
        Icon = SystemIcons.Information;

        var title = new Label
        {
            Text = "Big Picture Audio Switcher",
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 16),
        };

        var version = new Label
        {
            Text = $"Version {Updater.CurrentVersion}",
            AutoSize = true,
            Location = new Point(16, 44),
        };

        var blurb = new Label
        {
            Text = "Auto-switches your default audio output when Steam\n" +
                   "Big Picture mode opens and back again when it closes.",
            AutoSize = true,
            Location = new Point(16, 70),
        };

        var projectLink = new LinkLabel
        {
            Text = "Project page on GitHub",
            AutoSize = true,
            Location = new Point(16, 112),
        };
        projectLink.LinkClicked += (_, _) => OpenUrl(ProjectUrl);

        var coffeeLink = new LinkLabel
        {
            Text = "☕  Buy me a coffee",
            AutoSize = true,
            Location = new Point(16, 136),
        };
        coffeeLink.LinkClicked += (_, _) => OpenUrl(CoffeeUrl);

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(ClientSize.Width - 96, ClientSize.Height - 40),
            Size = new Size(80, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };

        Controls.AddRange(new Control[] { title, version, blurb, projectLink, coffeeLink, ok });
        AcceptButton = ok;
        CancelButton = ok;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to open URL {url}: {ex.Message}");
        }
    }
}
