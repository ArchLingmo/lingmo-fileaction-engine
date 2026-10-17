using System.Windows;
using Forms = System.Windows.Forms;

namespace FileAction.App.Shell;

/// <summary>The notification-area icon with the app menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;

    public TrayIcon(Action openSettings, Action openShelf, Action showHelp, Action<bool> setPaused, bool paused, Action exit)
    {
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/FileAction.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Nastavení…", null, (_, _) => openSettings());
        menu.Items.Add("Prázdný plovoucí panel", null, (_, _) => openShelf());
        menu.Items.Add("Jak to funguje", null, (_, _) => showHelp());
        _pause = new Forms.ToolStripMenuItem("Pozastavit gesta") { Checked = paused, CheckOnClick = true };
        _pause.CheckedChanged += (_, _) => setPaused(_pause.Checked);
        menu.Items.Add(_pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ukončit", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "FileAction",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
        Notifications.Sink = Show;
    }

    public bool Paused
    {
        set => _pause.Checked = value;
    }

    public void Show(string title, string text, bool error)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = string.IsNullOrWhiteSpace(text) ? " " : text;
        _icon.BalloonTipIcon = error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        Notifications.Sink = null;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
