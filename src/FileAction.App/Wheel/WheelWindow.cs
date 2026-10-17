using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileAction.App.Native;
using FileAction.App.UI;
using FileAction.Core;

namespace FileAction.App.Wheel;

internal enum WheelMode
{
    /// <summary>Opened mid-drag: the user drops the file on a slice. Never takes focus.</summary>
    Drag,

    /// <summary>Opened from the context menu or hotkey: click a slice or use the keyboard.</summary>
    Click,
}

/// <summary>The radial menu. One instance per appearance.</summary>
internal sealed class WheelWindow : Window
{
    public delegate IReadOnlyList<Operation> OperationSource(WheelKind wheel, IReadOnlyList<FileItem> items);

    private const double OuterRadius = 196;
    private const double InnerRadius = 74;
    private const double Rim = 8;
    private const double GapDegrees = 1.4;
    private const double Size = 2 * (OuterRadius + Rim);
    private static readonly WheelKind[] WheelOrder = [WheelKind.Convert, WheelKind.Tools, WheelKind.Actions];

    private readonly AppSettings _settings;
    private readonly OperationSource _source;
    private readonly Action<Operation, IReadOnlyList<FileItem>> _execute;
    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly List<Path> _slices = [];
    private readonly List<StackPanel> _labels = [];
    private readonly TextBlock _title = HubText(17, FontWeights.SemiBold);
    private readonly TextBlock _detail = HubText(11.5, FontWeights.Normal);
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _hubContent = new() { Width = InnerRadius * 1.7 };

    private IReadOnlyList<FileItem> _items;
    private IReadOnlyList<Operation> _ops = [];
    private WheelKind _wheel;
    private int _highlight = -1;
    private Win32.POINT _anchor;
    private bool _finished;
    private DispatcherTimer? _closeTimer;

    public WheelWindow(
        WheelMode mode,
        WheelKind wheel,
        IReadOnlyList<FileItem>? items,
        AppSettings settings,
        OperationSource source,
        Action<Operation, IReadOnlyList<FileItem>> execute)
    {
        Mode = mode;
        _wheel = wheel;
        _items = items ?? [];
        _settings = settings;
        _source = source;
        _execute = execute;

        Title = "FileAction";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = mode == WheelMode.Click;
        AllowDrop = true;
        Width = Size;
        Height = Size;
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        FontFamily = Theme.UiFont;
        Focusable = true;
        Content = _canvas;
        _canvas.RenderTransformOrigin = new Point(0.5, 0.5);
        _canvas.RenderTransform = new ScaleTransform(0.9, 0.9);
        _hubContent.Children.Add(_title);
        _hubContent.Children.Add(_detail);
        _hubContent.Children.Add(_tabs);

        SourceInitialized += (_, _) =>
        {
            var flags = Win32.WS_EX_TOOLWINDOW | (mode == WheelMode.Drag ? Win32.WS_EX_NOACTIVATE : 0);
            Win32.AddExStyle(Handle, flags);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionAtAnchor);

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, e) =>
        {
            SetHighlight(-1);
            e.Handled = true;
        };
        Drop += OnDrop;
        MouseMove += (sender, e) =>
        {
            if (Mode == WheelMode.Click)
                SetHighlight(HitTest(e.GetPosition(_canvas), out bool _));
        };
        MouseLeftButtonUp += OnClick;
        KeyDown += OnKeyDown;
        Deactivated += (_, _) =>
        {
            if (Mode == WheelMode.Click)
                Finish();
        };

        LoadOperations();
        Build();
    }

    public WheelMode Mode { get; }

    public bool IsFinished => _finished;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void ShowAt(Win32.POINT screenPoint)
    {
        _anchor = screenPoint;
        Show();
        PositionAtAnchor();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(140);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var scale = (ScaleTransform)_canvas.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, duration) { EasingFunction = ease });

        if (Mode == WheelMode.Click)
        {
            ForegroundHelper.Force(this);
            Keyboard.Focus(this);
            if (_ops.Count > 0)
                SetHighlight(0);
        }
    }

    private void PositionAtAnchor()
    {
        if (_finished || !Win32.GetWindowRect(Handle, out var rect))
            return;
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        var work = Win32.WorkAreaAt(_anchor);
        var x = Math.Clamp(_anchor.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        var y = Math.Clamp(_anchor.Y - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h));
        Win32.SetWindowPos(Handle, new IntPtr(-1), x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>True when a physical screen point lies on the wheel.</summary>
    public bool Contains(Win32.POINT p)
    {
        if (!Win32.GetWindowRect(Handle, out var rect))
            return false;
        var scale = (rect.Right - rect.Left) / Size;
        var cx = (rect.Left + rect.Right) / 2.0;
        var cy = (rect.Top + rect.Bottom) / 2.0;
        var radius = (OuterRadius + Rim) * scale;
        return Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2) <= radius * radius;
    }

    public void SwitchTo(WheelKind wheel)
    {
        if (_finished)
            return;
        _wheel = wheel;
        LoadOperations();
        Build();
        if (Mode == WheelMode.Click && _ops.Count > 0)
            SetHighlight(0);
    }

    /// <summary>The drag ended somewhere: close unless the drop landed on us.</summary>
    public void OnDragEnded()
    {
        if (Mode != WheelMode.Drag || _finished)
            return;
        _closeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => Finish(), Dispatcher);
        _closeTimer.Start();
    }

    public void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        _closeTimer?.Stop();
        Close();
    }

    /// <summary>Renders the wheel to a PNG without showing it (used by --self-test).</summary>
    public void RenderPreview(string file, int highlight)
    {
        _canvas.RenderTransform = Transform.Identity;
        if (highlight >= 0 && highlight < _ops.Count)
            SetHighlight(highlight);
        Content = null;
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x4F, 0x6F)), Width = Size + 40, Height = Size + 40 };
        root.Children.Add(_canvas);
        _canvas.HorizontalAlignment = HorizontalAlignment.Center;
        _canvas.VerticalAlignment = VerticalAlignment.Center;
        SelfTest.SaveElement(root, file);
    }

    // ------------------------------------------------------------------ data

    private void LoadOperations()
    {
        _ops = _items.Count == 0 ? [] : _source(_wheel, _items);
        _highlight = -1;
    }

    private static IReadOnlyList<FileItem>? ReadItems(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return null;
        return data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0
            ? paths.Select(p => new FileItem(p)).ToList()
            : null;
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_items.Count == 0)
        {
            var items = ReadItems(e.Data);
            if (items is null)
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            _items = items;
            LoadOperations();
            Build();
        }
        _closeTimer?.Stop();
        var index = HitTest(e.GetPosition(_canvas), out _);
        SetHighlight(index);
        e.Effects = index >= 0 ? SafeEffect(e.AllowedEffects) : DragDropEffects.None;
    }

    /// <summary>
    /// Copy keeps the original untouched. Never report Move: the drag source would delete the file.
    /// </summary>
    private static DragDropEffects SafeEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
        : (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link
        : DragDropEffects.None;

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var items = ReadItems(e.Data) ?? _items;
        var index = HitTest(e.GetPosition(_canvas), out _);
        var op = index >= 0 && index < _ops.Count ? _ops[index] : null;
        e.Effects = op is null ? DragDropEffects.None : SafeEffect(e.AllowedEffects);
        Finish();
        if (op is not null && items.Count > 0)
        {
            // Return to the drag source right away; do the work afterwards.
            Dispatcher.BeginInvoke(() => _execute(op, items));
        }
    }

    // ------------------------------------------------------------------ mouse & keyboard (click mode)

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (Mode != WheelMode.Click || e.Handled)
            return;
        var index = HitTest(e.GetPosition(_canvas), out var inHub);
        if (index >= 0)
            Run(index);
        else if (inHub)
            Finish();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        switch (key)
        {
            case Key.Escape:
                Finish();
                break;
            case Key.Left or Key.Up:
                Move(-1);
                break;
            case Key.Right or Key.Down:
                Move(1);
                break;
            case Key.Enter or Key.Space:
                if (_highlight >= 0)
                    Run(_highlight);
                break;
            case Key.Tab:
                var next = (Array.IndexOf(WheelOrder, _wheel) + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 2 : 1)) % WheelOrder.Length;
                SwitchTo(WheelOrder[next]);
                break;
            case Key.LeftShift or Key.RightShift:
                SwitchToKey(Input.ModifierKey.Shift);
                break;
            case Key.LeftCtrl or Key.RightCtrl:
                SwitchToKey(Input.ModifierKey.Ctrl);
                break;
            case Key.LeftAlt or Key.RightAlt:
                SwitchToKey(Input.ModifierKey.Alt);
                break;
            case >= Key.D1 and <= Key.D9:
                if (key - Key.D1 < _ops.Count)
                    Run(key - Key.D1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void SwitchToKey(Input.ModifierKey key)
    {
        if (_settings.WheelFor(key) is { } wheel && wheel != _wheel)
            SwitchTo(wheel);
    }

    private void Move(int delta)
    {
        if (_ops.Count == 0)
            return;
        SetHighlight(_highlight < 0 ? 0 : ((_highlight + delta) % _ops.Count + _ops.Count) % _ops.Count);
    }

    private void Run(int index)
    {
        var op = _ops[index];
        var items = _items;
        Finish();
        _execute(op, items);
    }

    // ------------------------------------------------------------------ geometry

    private static Point Polar(double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(Size / 2 + radius * Math.Sin(rad), Size / 2 - radius * Math.Cos(rad));
    }

    private int HitTest(Point p, out bool inHub)
    {
        var dx = p.X - Size / 2;
        var dy = p.Y - Size / 2;
        var r = Math.Sqrt(dx * dx + dy * dy);
        inHub = r < InnerRadius;
        if (inHub || r > OuterRadius + Rim || _ops.Count == 0)
            return -1;
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var step = 360.0 / _ops.Count;
        return (int)(((angle + step / 2) % 360) / step) % _ops.Count;
    }

    private static Geometry Sector(double a0, double a1)
    {
        var large = a1 - a0 > 180;
        var figure = new PathFigure { StartPoint = Polar(OuterRadius, a0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(OuterRadius, a1), new System.Windows.Size(OuterRadius, OuterRadius), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(InnerRadius, a1), true));
        figure.Segments.Add(new ArcSegment(Polar(InnerRadius, a0), new System.Windows.Size(InnerRadius, InnerRadius), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Ring()
    {
        var center = new Point(Size / 2, Size / 2);
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(center, OuterRadius, OuterRadius),
            new EllipseGeometry(center, InnerRadius, InnerRadius));
        ring.Freeze();
        return ring;
    }

    // ------------------------------------------------------------------ visuals

    private static TextBlock HubText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = Theme.Text,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Center,
        MaxWidth = InnerRadius * 1.6,
    };

    private void Build()
    {
        _canvas.Children.Clear();
        _slices.Clear();
        _labels.Clear();

        var backdrop = new Ellipse
        {
            Width = Size,
            Height = Size,
            Fill = Theme.Backdrop,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45 },
        };
        _canvas.Children.Add(backdrop);

        var n = _ops.Count;
        var step = n == 0 ? 360 : 360.0 / n;
        for (var i = 0; i < n; i++)
        {
            var a0 = i * step - step / 2;
            var path = new Path
            {
                Data = n == 1 ? Ring() : Sector(a0 + GapDegrees, a0 + step - GapDegrees),
                Fill = Theme.Slice,
            };
            _slices.Add(path);
            _canvas.Children.Add(path);
        }
        // Labels sit a little outside the middle of the ring, where slices are wider,
        // and shrink to the slice's width instead of spilling into the neighbours.
        var labelRadius = InnerRadius + (OuterRadius - InnerRadius) * 0.55;
        var labelWidth = n <= 2 ? 120 : Math.Clamp(2 * labelRadius * Math.Sin(step / 2 * Math.PI / 180) * 0.86, 48, 110);
        for (var i = 0; i < n; i++)
        {
            var label = SliceLabel(_ops[i]);
            var fit = new Viewbox
            {
                Child = label,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = labelWidth,
                MaxHeight = (OuterRadius - InnerRadius) * 0.8,
                IsHitTestVisible = false,
            };
            var mid = n == 1 ? new Point(Size / 2, Size / 2 - labelRadius) : Polar(labelRadius, i * step);
            fit.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(fit, mid.X - fit.DesiredSize.Width / 2);
            Canvas.SetTop(fit, mid.Y - fit.DesiredSize.Height / 2);
            _labels.Add(label);
            _canvas.Children.Add(fit);
        }

        var hub = new Ellipse
        {
            Width = 2 * (InnerRadius - 5),
            Height = 2 * (InnerRadius - 5),
            Fill = Theme.Hub,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(hub, Size / 2 - hub.Width / 2);
        Canvas.SetTop(hub, Size / 2 - hub.Height / 2);
        _canvas.Children.Add(hub);

        BuildTabs();
        UpdateHubText();
        _hubContent.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hubContent, Size / 2 - _hubContent.Width / 2);
        Canvas.SetTop(_hubContent, Size / 2 - _hubContent.DesiredSize.Height / 2);
        _canvas.Children.Add(_hubContent);
    }

    private static StackPanel SliceLabel(Operation op)
    {
        var panel = new StackPanel { MaxWidth = 96, IsHitTestVisible = false };
        var isFormat = op.Wheel == WheelKind.Convert && op.Label.Length <= 6 && op.Label.ToUpperInvariant() == op.Label;
        if (!isFormat)
        {
            panel.Children.Add(new TextBlock
            {
                Text = op.Glyph,
                FontFamily = Theme.Icons,
                FontSize = 18,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = op.Label,
            FontSize = isFormat ? 17 : 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(op.Subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = op.Subtitle,
                FontSize = 10.5,
                Foreground = Theme.Muted,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        return panel;
    }

    private void BuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var wheel in WheelOrder)
        {
            var key = _settings.KeyFor(wheel);
            if (key == Input.ModifierKey.None && wheel != _wheel)
                continue;
            var active = wheel == _wheel;
            var tab = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(2, 0, 2, 0),
                Background = active ? Theme.Accent : Brushes.Transparent,
                Cursor = Mode == WheelMode.Click ? Cursors.Hand : null,
                ToolTip = AppSettings.WheelName(wheel),
                Child = new TextBlock
                {
                    Text = key == Input.ModifierKey.None ? AppSettings.WheelName(wheel) : AppSettings.KeyName(key),
                    FontSize = 10.5,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = active ? Theme.TextOnAccent : Theme.Muted,
                },
            };
            if (Mode == WheelMode.Click)
            {
                var target = wheel;
                tab.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    SwitchTo(target);
                };
            }
            _tabs.Children.Add(tab);
        }
    }

    private void SetHighlight(int index)
    {
        if (index == _highlight)
            return;
        if (_highlight >= 0 && _highlight < _slices.Count)
            Paint(_highlight, false);
        _highlight = index;
        if (_highlight >= 0 && _highlight < _slices.Count)
            Paint(_highlight, true);
        UpdateHubText();
    }

    private void Paint(int index, bool on)
    {
        _slices[index].Fill = on ? Theme.Accent : Theme.Slice;
        foreach (var child in _labels[index].Children.OfType<TextBlock>())
        {
            var muted = child.FontSize < 11;
            child.Foreground = on ? Theme.TextOnAccent : muted ? Theme.Muted : Theme.Text;
        }
    }

    private void UpdateHubText()
    {
        if (_highlight >= 0 && _highlight < _ops.Count)
        {
            var op = _ops[_highlight];
            _title.Text = op.Label;
            _detail.Text = op.Subtitle ?? DescribeItems();
            return;
        }
        _title.Text = AppSettings.WheelName(_wheel);
        if (_items.Count == 0)
            _detail.Text = "Přetáhněte sem soubor";
        else if (_ops.Count == 0)
            _detail.Text = "Pro tento výběr nic není";
        else
            _detail.Text = DescribeItems();
    }

    private string DescribeItems()
    {
        if (_items.Count == 1)
            return _items[0].Name;
        var n = _items.Count;
        return n is >= 2 and <= 4 ? $"{n} položky" : $"{n} položek";
    }
}
