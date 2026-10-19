using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>
/// Sends a bug report or an idea to the author. Only what the user types is sent, plus the
/// FileAction and Windows versions (shown in the window), never files or paths.
/// </summary>
internal sealed class FeedbackWindow : Window
{
    public const string Website = "https://fileaction.apexpiksel.cz";
    private const string Endpoint = Website + "/api/feedback";

    private static FeedbackWindow? _open;
    private readonly TextBox _message = Box(multiline: true);
    private readonly TextBox _email = Box(multiline: false);
    private readonly TextBlock _status = Text("", 13, wrap: true);
    private readonly Border _send;
    private string _kind = "bug";
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
        root.Children.Add(Label(T("E-mail (nepovinný, jen pokud chcete odpověď)", "E-mail (optional, only if you want a reply)")));
        root.Children.Add(_email);

        var privacy = Text(T($"Spolu se zprávou se odešle verze FileAction {Shell.Updater.Current} a {WindowsVersion()}. Žádné soubory ani cesty.",
                             $"Sent along with your message: FileAction {Shell.Updater.Current} and {WindowsVersion()}. No files or paths."), 12, brush: Theme.Muted);
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
        web.MouseLeftButtonUp += (_, _) => Shell.Updater.Open(Website + "/#feedback");
        root.Children.Add(web);

        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
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
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FileAction/{Shell.Updater.Current}");
            using var response = await http.PostAsync(Endpoint, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                _message.Clear();
                Say(T("Díky! Zpráva je odeslaná.", "Thank you! Your message has been sent."), false);
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
