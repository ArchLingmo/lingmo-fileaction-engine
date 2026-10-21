using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static FileAction.Core.Loc;
using static FileAction.Linux.UI.Widgets;

namespace FileAction.Linux.UI;

/// <summary>
/// Sends a bug report or an idea to the author (the same endpoint as the Windows app). Only what the user
/// types is sent, plus the FileAction version and the distribution name (shown in the window).
/// </summary>
public sealed class FeedbackWindow : Window
{
    public const string Website = "https://fileaction.apexpiksel.cz";
    public const string SupportEmail = "support@apexpiksel.cz";
    private const string Endpoint = Website + "/api/feedback";

    private readonly TextBox _message = TextField("", 5000, multiline: true);
    private readonly TextBox _email = TextField("", 200);
    private readonly TextBlock _status = Text("", 13);
    private readonly Border _send;
    private Border _kinds = new();
    private readonly StackPanel _root = new() { Margin = new Thickness(28, 24, 28, 24) };
    private string _kind = "bug";
    private bool _sending;

    public FeedbackWindow()
    {
        ApplyLook(this);
        Title = T("FileAction – zpětná vazba", "FileAction – feedback");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _root.Children.Add(Text(T("Napsat zpětnou vazbu", "Send feedback"), 22, FontWeight.SemiBold));
        var intro = Text(T("Narazili jste na chybu, nebo vám něco chybí? Zpráva dorazí přímo autorovi.",
                           "Found a bug, or missing something? Your message goes straight to the author."), 13.5, brush: Palette.Muted);
        intro.Margin = new Thickness(0, 4, 0, 18);
        _root.Children.Add(intro);
        _root.Children.Add(_kinds);
        Kinds();

        _root.Children.Add(FieldLabel(T("Zpráva", "Message")));
        _message.Height = 150;
        _root.Children.Add(_message);
        _root.Children.Add(FieldLabel(T("E-mail (nepovinný, jen pokud chcete odpověď)", "E-mail (optional, only if you want a reply)")));
        _root.Children.Add(_email);

        var privacy = Text(T($"Spolu se zprávou se odešle verze FileAction {Cli.Version} a systém ({OsName()}). Žádné soubory ani cesty.",
                             $"Sent along with your message: FileAction {Cli.Version} and your system ({OsName()}). No files or paths."), 12, brush: Palette.Muted);
        privacy.Margin = new Thickness(0, 12, 0, 0);
        _root.Children.Add(privacy);

        _send = Button(T("Odeslat", "Send"), () => _ = SendAsync(), primary: true);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(_send);
        right.Children.Add(Button(T("Zavřít", "Close"), Close));
        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 12, 0);
        buttons.Children.Add(_status);
        _root.Children.Add(buttons);

        var web = Text(T("Nebo na webu: ", "Or on the web: ") + "fileaction.apexpiksel.cz/#feedback", 12, brush: Palette.Muted);
        web.Margin = new Thickness(0, 14, 0, 0);
        web.Cursor = new Cursor(StandardCursorType.Hand);
        web.PointerReleased += (_, _) => Desktop.Open(Website + "/#feedback");
        _root.Children.Add(web);

        var mail = Text(T("Nebo e-mailem: ", "Or by email: ") + SupportEmail, 12, brush: Palette.Muted);
        mail.Margin = new Thickness(0, 4, 0, 0);
        mail.Cursor = new Cursor(StandardCursorType.Hand);
        mail.PointerReleased += (_, _) => Desktop.Open("mailto:" + SupportEmail);
        _root.Children.Add(mail);

        Content = _root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        Opened += (_, _) => _message.Focus();
    }

    private void Kinds()
    {
        var index = _root.Children.IndexOf(_kinds);
        _kinds = Segmented(
            [(T("Chyba", "Bug"), "bug"), (T("Nápad", "Idea"), "idea"), (T("Otázka", "Question"), "question"), (T("Jiné", "Other"), "other")],
            _kind,
            k =>
            {
                _kind = k;
                Kinds();
            });
        _root.Children[index] = _kinds;
    }

    /// <summary>"Ubuntu 24.04 (6.8)": the distribution from /etc/os-release and the kernel version.</summary>
    public static string OsName()
    {
        string release;
        try
        {
            release = File.Exists("/etc/os-release") ? File.ReadAllText("/etc/os-release")
                : File.Exists("/usr/lib/os-release") ? File.ReadAllText("/usr/lib/os-release") : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            release = "";
        }
        return OsName(release, Environment.OSVersion.Version);
    }

    public static string OsName(string osRelease, Version kernel)
    {
        var values = osRelease.Split('\n')
            .Select(l => l.Trim().Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => g.First()[1].Trim().Trim('"', '\''));
        var name = values.GetValueOrDefault("NAME") is { Length: > 0 } n ? n : "Linux";
        if (values.GetValueOrDefault("VERSION_ID") is { Length: > 0 } version)
            name += " " + version;
        return $"{name} ({kernel.Major}.{Math.Max(0, kernel.Minor)})";
    }

    private void Say(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = error ? Palette.Error : Palette.Success;
    }

    private async Task SendAsync()
    {
        if (_sending)
            return;
        var message = (_message.Text ?? "").Trim();
        var email = (_email.Text ?? "").Trim();
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
                ["version"] = Cli.Version,
                ["os"] = OsName(),
                ["lang"] = Current == Core.Language.Czech ? "cs" : "en",
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FileAction/{Cli.Version}");
            using var response = await http.PostAsync(Endpoint, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                _message.Text = "";
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
