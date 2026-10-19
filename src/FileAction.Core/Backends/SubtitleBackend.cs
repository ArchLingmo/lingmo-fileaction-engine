using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>SRT ↔ WebVTT, and subtitles → plain text.</summary>
public static partial class SubtitleBackend
{
    public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

    [GeneratedRegex(@"^\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})")]
    private static partial Regex TimingRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    /// <summary>WebVTT-only markup: voice, class, language, ruby and karaoke timestamps (SRT players show them as text).</summary>
    [GeneratedRegex(@"</?(?:v|c|lang|ruby|rt)(?:[.\s][^>]*)?>|<\d{1,2}:\d{2}(?::\d{2})?\.\d{3}>")]
    private static partial Regex VttOnlyTagRegex();

    /// <summary>SRT-only markup that WebVTT doesn't know: ASS position codes and font tags.</summary>
    [GeneratedRegex(@"\{\\[^}]*\}|</?font[^>]*>")]
    private static partial Regex SrtOnlyTagRegex();

    /// <summary>The tags both formats share.</summary>
    [GeneratedRegex(@"^</?(?:b|i|u)>$")]
    private static partial Regex SharedTagRegex();

    public static List<Cue> Parse(string text)
    {
        var cues = new List<Cue>();
        var blocks = Regex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿'), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n').ToList();
            var timingIndex = lines.FindIndex(l => TimingRegex().IsMatch(l));
            if (timingIndex < 0)
                continue; // WEBVTT header, NOTE, STYLE, …
            var m = TimingRegex().Match(lines[timingIndex]);
            var body = string.Join("\n", lines.Skip(timingIndex + 1)).Trim();
            cues.Add(new Cue(ParseTimestamp(m.Groups[1].Value), ParseTimestamp(m.Groups[2].Value), body));
        }
        return cues;
    }

    public static TimeSpan ParseTimestamp(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        var hours = parts.Length == 3 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        // Whole milliseconds: going through a double turns 02,010 into 02.009.
        var secondParts = parts[^1].Split('.');
        var seconds = int.Parse(secondParts[0], CultureInfo.InvariantCulture);
        var millis = secondParts.Length > 1 ? int.Parse(secondParts[1].PadRight(3, '0')[..3], CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(0, hours, minutes, seconds, millis);
    }

    private static string Stamp(TimeSpan t, char separator) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}");

    public static string ToSrt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        var n = 1;
        foreach (var c in cues)
        {
            sb.Append(n++).Append('\n')
              .Append(Stamp(c.Start, ',')).Append(" --> ").Append(Stamp(c.End, ',')).Append('\n')
              .Append(VttToSrtText(c.Text)).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var c in cues)
        {
            sb.Append(Stamp(c.Start, '.')).Append(" --> ").Append(Stamp(c.End, '.')).Append('\n')
              .Append(SrtToVttText(c.Text)).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToText(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        string? previous = null;
        foreach (var c in cues)
        {
            var line = System.Net.WebUtility.HtmlDecode(TagRegex().Replace(SrtOnlyTagRegex().Replace(c.Text, ""), "")).Replace('\n', ' ').Trim();
            if (line.Length == 0 || line == previous)
                continue;
            sb.Append(line).Append('\n');
            previous = line;
        }
        return sb.ToString();
    }

    /// <summary>Text of a cue for SRT: WebVTT-only tags removed, entities (&amp;amp; &amp;lt;) turned back into characters.</summary>
    public static string VttToSrtText(string text)
    {
        var clean = VttOnlyTagRegex().Replace(text, "");
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match tag in TagRegex().Matches(clean))
        {
            sb.Append(System.Net.WebUtility.HtmlDecode(clean[last..tag.Index])).Append(tag.Value);
            last = tag.Index + tag.Length;
        }
        sb.Append(System.Net.WebUtility.HtmlDecode(clean[last..]));
        return sb.ToString();
    }

    /// <summary>
    /// Text of a cue for WebVTT: SRT-only codes removed, and '&amp;' and '&lt;' escaped unless they start a tag
    /// both formats share (otherwise a browser would read "&lt;3" as a tag and hide the rest of the line).
    /// </summary>
    public static string SrtToVttText(string text)
    {
        var clean = SrtOnlyTagRegex().Replace(text, "");
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match tag in TagRegex().Matches(clean))
        {
            sb.Append(EscapeVtt(clean[last..tag.Index]));
            sb.Append(SharedTagRegex().IsMatch(tag.Value) ? tag.Value : EscapeVtt(tag.Value));
            last = tag.Index + tag.Length;
        }
        sb.Append(EscapeVtt(clean[last..]));
        return sb.ToString();
    }

    private static string EscapeVtt(string text) =>
        System.Net.WebUtility.HtmlDecode(text).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static Task<string> ConvertAsync(FileItem item, string format, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, async tmp =>
        {
            // Older Czech subtitles are Windows-1250; reading them as UTF-8 would destroy the diacritics.
            var bytes = await File.ReadAllBytesAsync(item.Path, ctx.CancellationToken).ConfigureAwait(false);
            var cues = Parse(SpreadsheetBackend.DetectEncoding(bytes).GetString(bytes));
            if (cues.Count == 0)
                throw new ConversionException(T("V souboru nejsou žádné titulky.", "The file contains no subtitles."));
            var text = format switch
            {
                "srt" => ToSrt(cues),
                "vtt" => ToVtt(cues),
                _ => ToText(cues),
            };
            await File.WriteAllTextAsync(tmp, text, new UTF8Encoding(false), ctx.CancellationToken).ConfigureAwait(false);
        });
}
