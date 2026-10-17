using System.Windows;
using Forms = System.Windows.Forms;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell;

/// <summary>The notification-area icon with the app menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;

    public TrayIcon(Action openSettings, Action openClipboard, Action openShelf, Action showHelp, Action<bool> setPaused, bool paused, Action exit)
    {
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/FileAction.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("Kolo pro schránku", "Wheel for the clipboard"), null, (_, _) => openClipboard());
        menu.Items.Add(T("Nastavení…", "Settings…"), null, (_, _) => openSettings());
        menu.Items.Add(T("Prázdný plovoucí panel", "Empty floating shelf"), null, (_, _) => openShelf());
        menu.Items.Add(T("Jak to funguje", "How it works"), null, (_, _) => showHelp());
        _pause = new Forms.ToolStripMenuItem(T("Pozastavit gesta", "Pause gestures")) { Checked = paused, CheckOnClick = true };
        _pause.CheckedChanged += (_, _) => setPaused(_pause.Checked);
        menu.Items.Add(_pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("Ukončit", "Exit"), null, (_, _) => exit());

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
