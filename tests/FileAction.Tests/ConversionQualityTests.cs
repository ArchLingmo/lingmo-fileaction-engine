using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;

namespace FileAction.Tests;

public class SpreadsheetQualityTests
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    [Theory]
    [InlineData("1 234,50", 1234.5)]
    [InlineData("1 234,50", 1234.5)]
    [InlineData("0,1", 0.1)]
    [InlineData("-12,5", -12.5)]
    [InlineData("42", 42.0)]
    public void Czech_numbers_become_numbers(string text, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(SpreadsheetBackend.Interpret(text, Czech)), 6);

    [Theory]
    [InlineData("1E5")] // product code, not 100000
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("00123")] // leading zero: ZIP code, ID
    [InlineData("1234567890123456789")] // account number
    [InlineData("1 234,50 Kč")]
    public void Codes_and_odd_values_stay_text(string text) =>
        Assert.IsType<string>(SpreadsheetBackend.Interpret(text, Czech));

    [Fact]
    public void Czech_dates_and_percentages()
    {
        Assert.Equal(new DateTime(2026, 10, 19), SpreadsheetBackend.Interpret("19.10.2026", Czech));
        Assert.Equal(new DateTime(2026, 1, 2, 14, 30, 0), SpreadsheetBackend.Interpret("2.1.2026 14:30", Czech));
        Assert.Equal(new DateTime(2026, 10, 19), SpreadsheetBackend.Interpret("2026-10-19", CultureInfo.InvariantCulture));
        Assert.Equal(0.25, Assert.IsType<SpreadsheetBackend.Percent>(SpreadsheetBackend.Interpret("25 %", Czech)).Value, 6);
    }

    [Theory]
    [InlineData("\"Cena, Kč, s DPH\";Ks\n\"1 234,50\";3", ';')]
    [InlineData("a,b,c\n1,2,3", ',')]
    [InlineData("a\tb\n1\t2", '\t')]
    [InlineData("Jméno;Částka\nŘehoř;1 234,50\nŮna;99,90", ';')]
    public void Separator_is_guessed_outside_quotes(string text, char expected) =>
        Assert.Equal(expected, SpreadsheetBackend.GuessDelimiter(text));

    [Fact]
    public async Task Czech_csv_to_xlsx_keeps_types()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var csv = Path.Combine(dir, "faktury.csv");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            File.WriteAllText(csv, "Položka;Cena;Datum;Kód\nŽluťoučký kůň;1 234,50;19.10.2026;1E5\n", Encoding.GetEncoding(1250));
            var outputs = await SpreadsheetBackend.ConvertAsync(new FileItem(csv), "xlsx", new OperationContext { Tools = new ToolLocator(), OutputDirectory = dir });
            using var book = new XLWorkbook(outputs.Single());
            var ws = book.Worksheets.First();
            Assert.Equal("Žluťoučký kůň", ws.Cell(2, 1).GetText());
            Assert.Equal(1234.5, ws.Cell(2, 2).GetDouble(), 6);
            Assert.Equal(new DateTime(2026, 10, 19), ws.Cell(2, 3).GetDateTime());
            Assert.Equal("1E5", ws.Cell(2, 4).GetText());
            Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Xlsm_to_xlsx_keeps_formulas_and_dates()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var xlsm = Path.Combine(dir, "makro.xlsm");
            using (var book = new XLWorkbook())
            {
                var ws = book.Worksheets.Add("Faktury");
                ws.Cell(1, 1).Value = new DateTime(2026, 10, 19);
                ws.Cell(1, 2).Value = 1234.5;
                ws.Cell(1, 3).FormulaA1 = "B1*2";
                book.SaveAs(Path.Combine(dir, "makro.xlsx"));
            }
            File.Move(Path.Combine(dir, "makro.xlsx"), xlsm);
            var outDir = Path.Combine(dir, "out");
            var outputs = await SpreadsheetBackend.ConvertAsync(new FileItem(xlsm), "xlsx", new OperationContext { Tools = new ToolLocator(), OutputDirectory = outDir });
            using var result = new XLWorkbook(outputs.Single());
            var sheet = result.Worksheet("Faktury");
            Assert.Equal(new DateTime(2026, 10, 19), sheet.Cell(1, 1).GetDateTime());
            Assert.Equal("B1*2", sheet.Cell(1, 3).FormulaA1);
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public void Csv_out_uses_the_local_dialect()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Czech;
            var (delimiter, culture) = SpreadsheetBackend.CsvDialect();
            Assert.Equal(';', delimiter);
            Assert.Equal("1234,5", SpreadsheetBackend.CellText(1234.5, culture));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(',', SpreadsheetBackend.CsvDialect().Delimiter);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

public class SubtitleQualityTests
{
    [Fact]
    public void Milliseconds_are_exact() =>
        Assert.Equal(new TimeSpan(0, 1, 0, 2, 10), SubtitleBackend.ParseTimestamp("01:00:02,010"));

    [Fact]
    public void Vtt_markup_is_translated_for_srt() =>
        Assert.Equal("Ahoj & <i>vítej</i> <3", SubtitleBackend.VttToSrtText("<v Řehoř>Ahoj &amp; <i>vítej</i> &lt;3</v>"));

    [Fact]
    public void Srt_markup_is_escaped_for_vtt() =>
        Assert.Equal("Nahoře &amp; <b>dole</b> &lt;3", SubtitleBackend.SrtToVttText("{\\an8}Nahoře & <b>dole</b> <3"));

    [Fact]
    public async Task Windows_1250_subtitles_keep_their_diacritics()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var srt = Path.Combine(dir, "film.srt");
            File.WriteAllText(srt, "1\r\n00:00:01,000 --> 00:00:02,500\r\nPříliš žluťoučký kůň\r\n", Encoding.GetEncoding(1250));
            var vtt = await SubtitleBackend.ConvertAsync(new FileItem(srt), "vtt", new OperationContext { Tools = new ToolLocator() });
            Assert.Contains("Příliš žluťoučký kůň", File.ReadAllText(vtt));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }
}

public class ImageQualityTests
{
    [Fact]
    public void Grayscale_keeps_brightness()
    {
        using var image = new MagickImage(new MagickColor("#80898F"), 4, 4);
        ImageBackend.Grayscale(image);
        var pixel = image.GetPixels().GetPixel(0, 0).ToColor()!;
        Assert.InRange(pixel.R, 0x80, 0x90); // ~#888888, not the old #3D3D3D
    }

    [Fact]
    public async Task Animated_gif_stays_animated_after_an_edit()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var gif = Path.Combine(dir, "anim.gif");
            using (var frames = new MagickImageCollection())
            {
                foreach (var color in new[] { MagickColors.Red, MagickColors.Green, MagickColors.Blue })
                    frames.Add(new MagickImage(color, 40, 20) { AnimationDelay = 10 });
                frames.Write(gif, MagickFormat.Gif);
            }
            var output = Path.Combine(dir, "out.gif");
            await ImageBackend.EditAsync(gif, output, img => img.Rotate(90), 90, default);
            using var result = new MagickImageCollection(output);
            Assert.Equal(3, result.Count);
            Assert.Equal(20u, result[0].Width);
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public void Heic_edits_are_saved_as_jpg() => Assert.Equal("jpg", ImageBackend.OutputFormatFor("heic"));
}

public class MediaProbeTests
{
    [Fact]
    public void Reads_streams_from_ffmpeg_output()
    {
        string[] lines =
        [
            "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mov':",
            "  Duration: 00:00:12.50, start: 0.000000, bitrate: 5000 kb/s",
            "  Stream #0:0[0x1](und): Video: hevc (Main 10) (hvc1 / 0x31637668), yuv420p10le(tv, bt2020nc/bt2020/arib-std-b67), 1920x1080, 4800 kb/s, 30 fps",
            "      displaymatrix: rotation of -90.00 degrees",
            "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 160 kb/s",
            "  Stream #0:2[0x3](ces): Audio: aac (LC), 48000 Hz, mono, fltp, 96 kb/s",
            "  Stream #0:3[0x4](eng): Subtitle: mov_text (tx3g / 0x67337874), 0 kb/s",
            "Output #0, null, to 'pipe:':",
            "  Stream #0:0: Audio: pcm_s16le, 44100 Hz, stereo, s16, 1411 kb/s",
        ];
        var info = MediaProbe.Parse(lines);
        Assert.Equal(12.5, info.Duration);
        Assert.True(info.Hdr);
        Assert.True(info.Portrait);
        Assert.Equal(1080, info.ShortSide);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(2, info.AudioStreams);
        Assert.True(info.HasTextSubtitles);
        Assert.True(info.LossyAudio);
    }

    [Fact]
    public void Cover_art_is_not_mistaken_for_video()
    {
        string[] lines =
        [
            "  Duration: 00:03:00.00, start: 0.025057, bitrate: 320 kb/s",
            "  Stream #0:0: Audio: mp3 (mp3float), 44100 Hz, stereo, fltp, 320 kb/s",
            "  Stream #0:1: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 500x500 [SAR 1:1 DAR 1:1], 90k tbr, 90k tbn (attached pic)",
        ];
        var info = MediaProbe.Parse(lines);
        Assert.True(info.HasCover);
        Assert.False(info.HasVideo);
        var args = FfmpegBackend.CodecArgs("mp3", new Settings(), info, false);
        Assert.Contains("attached_pic", args);
        Assert.DoesNotContain("-vn", args);
    }
}

public class PrivacyAndSizeTests
{
    [Fact]
    public async Task Conversion_removes_the_gps_position_but_keeps_other_exif()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var jpg = Path.Combine(dir, "photo.jpg");
            using (var image = new MagickImage("plasma:", 200, 100))
            {
                var exif = new ExifProfile();
                exif.SetValue(ExifTag.Make, "TestCam");
                exif.SetValue(ExifTag.GPSLatitudeRef, "N");
                exif.SetValue(ExifTag.GPSLatitude, [new Rational(50, 1), new Rational(5, 1), new Rational(0, 1)]);
                image.SetProfile(exif);
                image.Write(jpg, MagickFormat.Jpeg);
            }
            var webp = Path.Combine(dir, "photo.webp");
            await ImageBackend.ConvertAsync(jpg, webp, "webp", new Settings(), default);
            using var result = new MagickImage(webp);
            var kept = result.GetExifProfile();
            Assert.NotNull(kept);
            Assert.Null(kept!.GetValue(ExifTag.GPSLatitude));
            Assert.Equal("TestCam", kept.GetValue(ExifTag.Make)?.Value);

            var keep = Path.Combine(dir, "keep.webp");
            await ImageBackend.ConvertAsync(jpg, keep, "webp", new Settings { StripLocation = false }, default);
            using var withGps = new MagickImage(keep);
            Assert.NotNull(withGps.GetExifProfile()?.GetValue(ExifTag.GPSLatitude));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Icons_are_small()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var png = Path.Combine(dir, "logo.png");
            using (var image = new MagickImage("plasma:", 600, 200))
                image.Write(png, MagickFormat.Png);
            var ico = Path.Combine(dir, "logo.ico");
            await ImageBackend.ConvertAsync(png, ico, "ico", new Settings(), default);
            Assert.True(new FileInfo(ico).Length < 200_000, $"{new FileInfo(ico).Length} bytes");
            using var icon = new MagickImageCollection(ico);
            Assert.All(icon, frame => Assert.Equal(frame.Width, frame.Height));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }
}
