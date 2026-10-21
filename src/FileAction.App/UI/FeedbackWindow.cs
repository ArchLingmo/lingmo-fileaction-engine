using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>
/// Sends a bug report or an idea to the author. Only what the user types and the images they attach
/// are sent, plus the FileAction and Windows versions (shown in the window), never other files or paths.
/// Images are shrunk and re-encoded here, which also drops their metadata (EXIF location and the like).
/// </summary>
internal sealed class FeedbackWindow : Window
{
    public const string Website = "https://fileaction.apexpiksel.cz";
    public const string SupportEmail = "support@apexpiksel.cz";
    private const string Endpoint = Website + "/api/feedback";

    private static FeedbackWindow? _open;
    private readonly TextBox _message = Box(multiline: true);
    private readonly TextBox _email = Box(multiline: false);
    private readonly TextBlock _status = Text("", 13, wrap: true);
    private readonly System.Windows.Controls.Button _send;
    private string _kind = "bug";
    private const int MaxImages = 3;
    private const int MaxBytes = 2_400_000;   // the server takes 2.5 MB per image
    private const int MaxSide = 2560;
    private readonly List<(string Name, string Type, byte[] Data)> _images = [];
    private readonly WrapPanel _thumbs = new() { Orientation = Orientation.Horizontal };
    private bool _sending;

    internal FeedbackWindow()
    {
        Theme.Refresh();
        Title = T("FileAction – zpětná vazba", "FileAction – feedback");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.ico"));
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;

        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        var title = Text(T("Napsat zpětnou vazbu", "Send feedback"), 24, FontWeights.SemiBold);
        title.FontFamily = Theme.DisplayFont;
        root.Children.Add(title);
        var intro = Text(T("Narazili jste na chybu, nebo vám něco chybí? Zpráva dorazí přímo autorovi.",
                           "Found a bug, or missing something? Your message goes straight to the author."), 13.5, brush: Theme.Muted);
        intro.Margin = new Thickness(0, 4, 0, 18);
        root.Children.Add(intro);

        var kinds = Segmented(
            [(T("Chyba", "Bug"), "bug"), (T("Nápad", "Idea"), "idea"), (T("Otázka", "Question"), "question"), (T("Jiné", "Other"), "other")],
            _kind, k => _kind = k);
        kinds.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(kinds);

        root.Children.Add(Label(T("Zpráva", "Message")));
        _message.Height = 150;
        root.Children.Add(_message);
        // Screenshots: pick a file or paste one (Win+Shift+S, then Ctrl+V).
        root.Children.Add(Label(T("Snímky obrazovky (nepovinné, nejvýš 3)", "Screenshots (optional, up to 3)")));
        var add = Button(T("Přidat obrázek…", "Add image…"), PickImages, glyph: "\uEB9F");
        var paste = Text(T("nebo Ctrl+V", "or Ctrl+V"), 12, brush: Theme.Muted);
        paste.VerticalAlignment = VerticalAlignment.Center;
        paste.Margin = new Thickness(12, 0, 0, 0);
        var shotsRow = new StackPanel { Orientation = Orientation.Horizontal };
        shotsRow.Children.Add(add);
        shotsRow.Children.Add(paste);
        root.Children.Add(shotsRow);
        _thumbs.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_thumbs);

        root.Children.Add(Label(T("E-mail (nepovinný, jen pokud chcete odpověď)", "E-mail (optional, only if you want a reply)")));
        root.Children.Add(_email);

        var privacy = Text(T($"Spolu se zprávou se odešle verze FileAction {Shell.Updater.Current} a {WindowsVersion()}, případně přiložené obrázky (zkontrolujte, jestli na nich nejsou osobní údaje). Žádné jiné soubory ani cesty.",
                             $"Sent along with your message: FileAction {Shell.Updater.Current}, {WindowsVersion()} and any images you attach (check them for personal details). No other files or paths."), 12, brush: Theme.Muted);
        privacy.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(privacy);

        _send = Button(T("Odeslat", "Send"), () => _ = SendAsync(), primary: true, glyph: "");
        var cancel = Button(T("Zavřít", "Close"), Close);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_send);
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 12, 0);
        buttons.Children.Add(_status);
        root.Children.Add(buttons);

        var web = Text(T("Nebo na webu: ", "Or on the web: ") + "fileaction.apexpiksel.cz/#feedback", 12, brush: Theme.Muted);
        web.Margin = new Thickness(0, 14, 0, 0);
        web.Cursor = Cursors.Hand;
        root.Children.Add(Clickable(web, T("Formulář na webu", "Form on the website"), () => Shell.Updater.Open(Website + "/#feedback")));

        var mail = Text(T("Nebo e-mailem: ", "Or by email: ") + SupportEmail, 12, brush: Theme.Muted);
        mail.Margin = new Thickness(0, 4, 0, 0);
        mail.Cursor = Cursors.Hand;
        root.Children.Add(Clickable(mail, T("Napsat e-mail", "Write an email"), () => Shell.Updater.Open("mailto:" + SupportEmail)));

        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        // Ctrl+V with an image (or image files) on the clipboard attaches it; text pastes as usual.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                Attach(() => Encode(image, preferPng: true));
                e.Handled = true;
            }
            else if (Clipboard.ContainsFileDropList())
            {
                foreach (var file in Clipboard.GetFileDropList().Cast<string>().Where(IsImage))
                    Attach(() => FromFile(file));
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            ForegroundHelper.Force(this);
            Keyboard.Focus(_message);
        };
        Closed += (_, _) => _open = null;
    }

    public static void ShowSingle()
    {
        _open ??= new FeedbackWindow();
        _open.Show();
        _open.Activate();
    }

    private static bool IsImage(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" or ".heic";

    private void PickImages()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = T("Obrázky", "Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        foreach (var file in dialog.FileNames)
            Attach(() => FromFile(file));
    }

    private static (string, string, byte[]) FromFile(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return Encode(frame, preferPng: ext is ".png" or ".bmp" or ".gif");
    }

    /// <summary>Shrinks to at most 2560 px and re-encodes (new frame = no metadata): PNG for screenshots when it fits, JPEG otherwise.</summary>
    private static (string, string, byte[]) Encode(BitmapSource source, bool preferPng)
    {
        double k = Math.Min(1, (double)MaxSide / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource scaled = k < 1 ? new TransformedBitmap(source, new ScaleTransform(k, k)) : source;
        if (preferPng)
        {
            var png = Save(new PngBitmapEncoder(), scaled);
            if (png.Length <= MaxBytes)
                return ("screenshot.png", "image/png", png);
        }
        // JPEG has no transparency: flatten onto white.
        var flat = new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0);
        foreach (var quality in new[] { 85, 72, 60 })
        {
            var jpg = Save(new JpegBitmapEncoder { QualityLevel = quality }, flat);
            if (jpg.Length <= MaxBytes)
                return ("screenshot.jpg", "image/jpeg", jpg);
        }
        throw new InvalidOperationException("too big");
    }

    private static byte[] Save(BitmapEncoder encoder, BitmapSource source)
    {
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private void Attach(Func<(string Name, string Type, byte[] Data)> make)
    {
        if (_images.Count >= MaxImages)
        {
            Say(T("Nejvýš 3 obrázky.", "Up to 3 images."), true);
            return;
        }
        (string Name, string Type, byte[] Data) image;
        try
        {
            image = make();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Say(T("Tenhle obrázek nejde použít.", "That image can't be used."), true);
            return;
        }
        _images.Add(image);
        var preview = new BitmapImage();
        preview.BeginInit();
        preview.StreamSource = new MemoryStream(image.Data);
        preview.DecodePixelHeight = 128;
        preview.CacheOption = BitmapCacheOption.OnLoad;
        preview.EndInit();
        var thumb = new Grid { Width = 96, Height = 64, Margin = new Thickness(0, 0, 8, 0) };
        thumb.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Background = new ImageBrush(preview) { Stretch = Stretch.UniformToFill },
        });
        var remove = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Cursor = Cursors.Hand,
            ToolTip = T("Odebrat", "Remove"),
            Child = new TextBlock { Text = "\uE711", FontFamily = Theme.Icons, FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        thumb.Children.Add(Clickable(remove, T("Odebrat obrázek", "Remove picture"), () =>
        {
            _images.Remove(image);
            _thumbs.Children.Remove(thumb);
        }));
        _thumbs.Children.Add(thumb);
        _status.Text = "";
    }

    /// <summary>For the self-test: attaches an image as if it were pasted.</summary>
    internal void AttachForPreview(BitmapSource image) => Attach(() => Encode(image, preferPng: true));

    private static TextBlock Label(string text)
    {
        var label = Text(text, 13, brush: Theme.Muted);
        label.Margin = new Thickness(0, 16, 0, 6);
        return label;
    }

    private static TextBox Box(bool multiline) => new()
    {
        Padding = new Thickness(10, 8, 10, 8),
        FontSize = 14,
        Background = Theme.Card,
        Foreground = Theme.Text,
        CaretBrush = Theme.Text,
        BorderBrush = Theme.PanelBorder,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
        MaxLength = multiline ? 5000 : 200,
    };

    private static string WindowsVersion()
    {
        var v = Environment.OSVersion.Version;
        return $"Windows {(v.Major == 10 && v.Build >= 22000 ? "11" : v.Major == 10 ? "10" : v.ToString(2))} ({v.Build})";
    }

    private void Say(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = error ? Theme.Error : Theme.Success;
    }

    private async Task SendAsync()
    {
        if (_sending)
            return;
        var message = _message.Text.Trim();
        var email = _email.Text.Trim();
        if (message.Length < 3)
        {
            Say(T("Napište prosím aspoň pár slov.", "Please write a few words first."), true);
            return;
        }
        if (email.Length > 0 && (!email.Contains('@') || email.Contains(' ')))
        {
            Say(T("Ten e-mail nevypadá správně.", "That e-mail doesn't look right."), true);
            return;
        }

        _sending = true;
        _send.Opacity = 0.5;
        Say(T("Odesílám…", "Sending…"), false);
        try
        {
            var body = new JsonObject
            {
                ["kind"] = _kind,
                ["message"] = message,
                ["email"] = email,
                ["source"] = "app",
                ["version"] = Shell.Updater.Current.ToString(),
                ["os"] = WindowsVersion(),
                ["lang"] = Current == Core.Language.Czech ? "cs" : "en",
                ["attachments"] = new JsonArray(_images.Select(i => (JsonNode)new JsonObject
                {
                    ["name"] = i.Name,
                    ["type"] = i.Type,
                    ["data"] = Convert.ToBase64String(i.Data),
                }).ToArray()),
            };
            // With screenshots this can be a few MB, which takes a while on a slow uplink.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FileAction/{Shell.Updater.Current}");
            using var response = await http.PostAsync(Endpoint, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                _message.Clear();
                _images.Clear();
                _thumbs.Children.Clear();
                Say(T("Díky! Zpráva je odeslaná.", "Thank you! Your message has been sent."), false);
            }
            else if ((int)response.StatusCode == 413 || (await response.Content.ReadAsStringAsync()).Contains("\"attachment"))
            {
                Say(T("Server obrázek nepřijal. Zkuste jiný.", "The server didn't accept an image. Try a different one."), true);
            }
            else if ((int)response.StatusCode == 429)
            {
                Say(T("Moc zpráv za krátkou dobu. Zkuste to za pár minut.", "Too many messages in a short time. Try again in a few minutes."), true);
            }
            else
            {
                Say(T("Odeslání se nepovedlo. Zkuste to prosím později.", "Sending failed. Please try again later."), true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Say(T("Odeslání se nepovedlo. Zkontrolujte připojení.", "Sending failed. Check your connection."), true);
        }
        finally
        {
            _sending = false;
            _send.Opacity = 1;
        }
    }
}
