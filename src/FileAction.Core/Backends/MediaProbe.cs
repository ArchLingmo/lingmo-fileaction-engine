using System.Globalization;
using System.Text.RegularExpressions;

namespace FileAction.Core.Backends;

/// <summary>What a media file contains, read from ffmpeg's own description of its streams.</summary>
public sealed record MediaInfo
{
    public double? Duration { get; init; }
    public int? SampleRate { get; init; }
    public int Channels { get; init; }
    public int? AudioBitrate { get; init; }
    public string? AudioCodec { get; init; }
    public string? SampleFormat { get; init; }
    public int AudioStreams { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool Hdr { get; init; }
    public bool HasCover { get; init; }
    public bool HasVideo { get; init; }
    public List<string> SubtitleCodecs { get; init; } = [];

    /// <summary>Width and height as shown (a phone video stored landscape with a 90° rotation is portrait).</summary>
    public bool Portrait { get; init; }

    public bool LossyAudio => AudioCodec is "mp3" or "aac" or "opus" or "vorbis" or "wmav2" or "wmav1" or "ac3" or "eac3";

    /// <summary>24-bit (or float) source audio; 16-bit and lossy sources are written as 16-bit.</summary>
    public bool HighResAudio => !LossyAudio && SampleFormat is { } f && (f.StartsWith("s32") || f.StartsWith("s24") || f.StartsWith("flt") || f.StartsWith("dbl"));

    public int ShortSide => Math.Min(Width ?? 0, Height ?? 0);

    /// <summary>Subtitles stored as text (SRT, ASS, WebVTT, mov_text), which other containers can carry.</summary>
    public bool HasTextSubtitles => SubtitleCodecs.Any(c => c is "subrip" or "srt" or "ass" or "ssa" or "mov_text" or "webvtt" or "text");
}

public static partial class MediaProbe
{
    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"Stream #\d+:\d+(?:\[[^\]]*\])?(?:\([^)]*\))?: (Audio|Video|Subtitle): (\w+)(.*)$")]
    private static partial Regex StreamRegex();

    [GeneratedRegex(@"(\d+) Hz")]
    private static partial Regex HzRegex();

    [GeneratedRegex(@"(\d+) kb/s")]
    private static partial Regex KbpsRegex();

    [GeneratedRegex(@"\b(\d{2,5})x(\d{2,5})\b")]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"displaymatrix: rotation of (-?\d+(?:\.\d+)?)")]
    private static partial Regex RotationRegex();

    [GeneratedRegex(@"Hz, ([^,]+), (\w+)")]
    private static partial Regex LayoutRegex();

    /// <summary>
    /// Runs <c>ffmpeg -i</c> (ffprobe is not always there) and reads the stream lines. Never throws: an
    /// unreadable file gives an empty description and the conversion reports the real error.
    /// </summary>
    public static async Task<MediaInfo> ProbeAsync(OperationContext ctx, string path)
    {
        var lines = new List<string>();
        try
        {
            var ffmpeg = ctx.Tools.Require(ExternalTool.Ffmpeg);
            await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", path, "-map", "0:a:0?", "-t", "0", "-f", "null", "-"],
                ctx.CancellationToken, lines.Add).ConfigureAwait(false);
        }
        catch (ConversionException)
        {
            // ffmpeg describes the input before it fails; what was printed is enough.
        }
        return Parse(lines);
    }

    public static MediaInfo Parse(IReadOnlyList<string> lines)
    {
        double? duration = null;
        int? rate = null, bitrate = null, width = null, height = null;
        string? audioCodec = null, sampleFormat = null;
        int channels = 0, audioStreams = 0;
        bool hdr = false, cover = false, video = false, rotated = false;
        var subtitles = new List<string>();
        var inOutput = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("Output #", StringComparison.Ordinal))
                inOutput = true;
            if (inOutput)
                continue;
            if (duration is null && DurationRegex().Match(line) is { Success: true } d)
                duration = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                    + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
            if (RotationRegex().Match(line) is { Success: true } r && Math.Abs(Math.Abs(double.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture)) - 90) < 1)
                rotated = true;
            if (StreamRegex().Match(line) is not { Success: true } s)
                continue;
            var kind = s.Groups[1].Value;
            var codec = s.Groups[2].Value;
            var rest = s.Groups[3].Value;
            if (kind == "Audio")
            {
                audioStreams++;
                if (audioStreams > 1)
                    continue;
                audioCodec = codec;
                if (HzRegex().Match(rest) is { Success: true } hz)
                    rate = int.Parse(hz.Groups[1].Value, CultureInfo.InvariantCulture);
                if (KbpsRegex().Match(rest) is { Success: true } kb)
                    bitrate = int.Parse(kb.Groups[1].Value, CultureInfo.InvariantCulture);
                if (LayoutRegex().Match(rest) is { Success: true } layout)
                {
                    var name = layout.Groups[1].Value.Trim();
                    channels = name switch { "mono" => 1, "stereo" => 2, _ => name.StartsWith("5.1") ? 6 : name.StartsWith("7.1") ? 8 : 2 };
                    sampleFormat = layout.Groups[2].Value;
                }
            }
            else if (kind == "Video")
            {
                if (rest.Contains("attached pic", StringComparison.Ordinal))
                {
                    cover = true;
                    continue;
                }
                if (video)
                    continue;
                video = true;
                if (SizeRegex().Match(rest) is { Success: true } size)
                {
                    width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                hdr = rest.Contains("smpte2084", StringComparison.Ordinal) || rest.Contains("arib-std-b67", StringComparison.Ordinal);
            }
            else
            {
                subtitles.Add(codec);
            }
        }
        var portrait = width is not null && height is not null && (rotated ? width > height : height > width);
        return new MediaInfo
        {
            Duration = duration,
            SampleRate = rate,
            Channels = channels,
            AudioBitrate = bitrate,
            AudioCodec = audioCodec,
            SampleFormat = sampleFormat,
            AudioStreams = audioStreams,
            Width = width,
            Height = height,
            Hdr = hdr,
            HasCover = cover,
            HasVideo = video,
            SubtitleCodecs = subtitles,
            Portrait = portrait,
        };
    }

    private static bool? _hasToneMapping;

    /// <summary>True when this ffmpeg can tone-map HDR to SDR (zscale + tonemap; the essentials build can).</summary>
    public static async Task<bool> CanToneMapAsync(OperationContext ctx)
    {
        if (_hasToneMapping is { } known)
            return known;
        var output = new List<string>();
        try
        {
            var stdout = await ProcessRunner.RunAsync(ctx.Tools.Require(ExternalTool.Ffmpeg), ["-hide_banner", "-filters"], ctx.CancellationToken, output.Add)
                .ConfigureAwait(false);
            output.Add(stdout);
        }
        catch (ConversionException)
        {
        }
        var all = string.Join("\n", output);
        _hasToneMapping = all.Contains(" zscale ", StringComparison.Ordinal) && all.Contains(" tonemap ", StringComparison.Ordinal);
        return _hasToneMapping.Value;
    }
}
