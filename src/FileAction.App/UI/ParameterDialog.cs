using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>Asks for the values an operation needs (e.g. trim start and end).</summary>
internal sealed class ParameterDialog : Window
{
    private readonly Dictionary<string, TextBox> _boxes = new();

    public ParameterDialog(Operation op, IReadOnlyList<FileItem> items)
    {
        ThemeMode = ThemeMode.System;
        Title = op.Label;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        MinWidth = 340;

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = items.Count == 1 ? items[0].Name : Items(items.Count),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420,
        });

        foreach (var p in op.Parameters)
        {
            stack.Children.Add(new TextBlock { Text = p.Label, Margin = new Thickness(0, 6, 0, 4) });
            var box = new TextBox { Text = p.DefaultValue, MinWidth = 300 };
            _boxes[p.Key] = box;
            stack.Children.Add(box);
            if (p.Hint is not null)
                stack.Children.Add(new TextBlock { Text = p.Hint, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) });
        }

        var ok = new Button { Content = T("Spustit", "Run"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = T("Zrušit", "Cancel"), IsCancel = true, MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) =>
        {
            ForegroundHelper.Force(this);
            if (_boxes.Values.FirstOrDefault() is { } first)
            {
                first.Focus();
                first.SelectAll();
                Keyboard.Focus(first);
            }
        };
    }

    public IReadOnlyDictionary<string, string> Values => _boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
}
