using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FileAction.App.Native;
using FileAction.App.UI;
using static FileAction.Core.Loc;

namespace FileAction.App.Jobs;

/// <summary>Progress cards in the bottom-right corner. Never steals focus.</summary>
internal sealed class JobsWindow : Window
{
    private readonly StackPanel _cards = new();

    public JobsWindow()
    {
        Title = T("FileAction – průběh", "FileAction – progress");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        FontFamily = Theme.UiFont;
        Content = _cards;
        SourceInitialized += (_, _) =>
            Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        SizeChanged += (_, _) => PlaceInCorner();
    }

    public JobCard Add(string title, string detail, Action cancel)
    {
        var card = new JobCard(title, detail, cancel, Remove);
        _cards.Children.Insert(0, card);
        if (!IsVisible)
            Show();
        PlaceInCorner();
        return card;
    }

    private void Remove(JobCard card)
    {
        _cards.Children.Remove(card);
        if (_cards.Children.Count == 0)
            Hide();
    }

    private void PlaceInCorner()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 8;
        Top = work.Bottom - ActualHeight - 8;
    }
}

internal sealed class JobCard : Border
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _status;
    private readonly StackPanel _buttons;
    private readonly Action<JobCard> _remove;
    private readonly Action _cancel;
    private bool _done;

    public JobCard(string title, string detail, Action cancel, Action<JobCard> remove)
    {
        _cancel = cancel;
        _remove = remove;
        Background = Theme.Panel;
        BorderBrush = Theme.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Margin = new Thickness(8, 4, 8, 4);
        Padding = new Thickness(14, 10, 10, 12);
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.35 };

        var titleText = new TextBlock { Text = title, Foreground = Theme.Text, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var detailText = new TextBlock { Text = detail, Foreground = Theme.Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 6) };
        _bar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, IsIndeterminate = true, Foreground = Theme.Accent, Background = Theme.AccentSoft, BorderThickness = new Thickness(0) };
        _status = new TextBlock { Foreground = Theme.Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        _buttons.Children.Add(LinkButton(T("Zrušit", "Cancel"), () => _cancel()));

        var close = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 10, Foreground = Theme.Muted },
            Width = 22,
            Height = 22,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = T("Zavřít", "Close"),
        };
        close.Click += (_, _) =>
        {
            if (!_done)
                _cancel();
            _remove(this);
        };

        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(titleText);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(detailText);
        stack.Children.Add(_bar);
        stack.Children.Add(_status);
        stack.Children.Add(_buttons);
        Child = stack;
    }

    public void Report(double value)
    {
        if (_done)
            return;
        _bar.IsIndeterminate = value <= 0;
        _bar.Value = value;
    }

    public void Complete(IReadOnlyList<string> outputs, string? warning)
    {
        _done = true;
        _bar.IsIndeterminate = false;
        _bar.Value = 1;
        _bar.Foreground = Theme.Success;
        _status.Visibility = Visibility.Visible;
        _status.Foreground = warning is null ? Theme.Success : Theme.Error;
        _status.Text = warning ?? (outputs.Count == 1 ? T("Hotovo: ", "Done: ") + Path.GetFileName(outputs[0]) : T($"Hotovo: {outputs.Count} výstupů", $"Done: {outputs.Count} outputs"));
        _buttons.Children.Clear();
        if (outputs.Count > 0)
        {
            var first = outputs[0];
            if (File.Exists(first) && outputs.Count == 1)
                _buttons.Children.Add(LinkButton(T("Otevřít", "Open"), () => Shell(first)));
            _buttons.Children.Add(LinkButton(T("Zobrazit ve složce", "Show in folder"), () => Reveal(first)));
        }
        if (warning is null)
            CloseLater(TimeSpan.FromSeconds(8));
    }

    public void Fail(string message)
    {
        _done = true;
        _bar.IsIndeterminate = false;
        _bar.Foreground = Theme.Error;
        _status.Visibility = Visibility.Visible;
        _status.Foreground = Theme.Error;
        _status.Text = message;
        _buttons.Children.Clear();
        _buttons.Children.Add(LinkButton(T("Kopírovat chybu", "Copy error"), () =>
        {
            try
            {
                Clipboard.SetText(message);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        }));
    }

    public void Cancelled()
    {
        _done = true;
        _remove(this);
    }

    private void CloseLater(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsMouseOver)
                _remove(this);
            else
                CloseLater(TimeSpan.FromSeconds(3));
        };
        timer.Start();
    }

    private static Button LinkButton(string text, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, Foreground = Theme.Accent, FontSize = 12 },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => click();
        return button;
    }

    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void Shell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
