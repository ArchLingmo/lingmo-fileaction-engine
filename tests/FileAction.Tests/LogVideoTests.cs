using System.Globalization;
using System.Text.Json;
using FileAction.Core;
using FileAction.Core.Backends;
using FileAction.Core.Operations;

namespace FileAction.Tests;

public class LogVideoTests
{
    [Fact]
    public void SLog3_curve_round_trips_and_has_Sonys_reference_points()
    {
        Assert.Equal(420.0 / 1023, LogVideo.LinearToSLog3(0.18), 6);
        Assert.Equal(95.0 / 1023, LogVideo.LinearToSLog3(0), 6);
        foreach (var x in new[] { 0.0, 0.005, 0.18, 0.9, 10 })
            Assert.Equal(x, LogVideo.SLog3ToLinear(LogVideo.LinearToSLog3(x)), 6);
    }

    [Theory]
    [InlineData(LogVideo.Gamut.SGamut3Cine)]
    [InlineData(LogVideo.Gamut.SGamut3)]
    public void Middle_grey_stays_neutral_and_lands_near_Sonys_LC709(LogVideo.Gamut gamut)
    {
        var grey = LogVideo.LinearToSLog3(0.18);
        var (r, g, b) = LogVideo.Grade(grey, grey, grey, LogVideo.ToRec709(gamut));
        Assert.InRange(g, 0.40, 0.45);
        Assert.Equal(g, r, 3);
        Assert.Equal(g, b, 3);
        // Highlights roll off instead of clipping: the log white (90 %) is bright but not at the limit.
        var white = LogVideo.LinearToSLog3(0.9);
        Assert.InRange(LogVideo.Grade(white, white, white, LogVideo.ToRec709(gamut)).G, 0.8, 0.98);
    }

    [Fact]
    public void Cube_has_every_grid_point()
    {
        var cube = LogVideo.SLog3Cube(LogVideo.Gamut.SGamut3Cine, 17);
        Assert.Contains("LUT_3D_SIZE 17", cube);
        Assert.Equal(17 * 17 * 17 + 2, cube.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Filter_paths_escape_drive_colons()
    {
        Assert.Equal(@"'C\:/Temp/x/lut.cube'", LogVideo.FilterPath(@"C:\Temp\x\lut.cube"));
    }
}

/// <summary>A synthetic Sony clip: 10-bit S-Log3, the camera's metadata, a recording date. Skipped without ffmpeg.</summary>
[Trait("Category", "Integration")]
public class LogVideoIntegrationTests : IDisposable
{
    private readonly string _dir = OutputPaths.TempDirectory();
    private readonly ToolLocator _tools = new();
    private static readonly DateTime Recorded = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    public void Dispose() => OutputPaths.TryDelete(_dir);

    private OperationContext Context(string name) => new() { Tools = _tools, OutputDirectory = Path.Combine(_dir, "out", name) };

    /// <summary>Left half S-Log3 middle grey (code 420 of 1023), right half black (code 95).</summary>
    private string MakeClip(string name, bool sony)
    {
        var path = Path.Combine(_dir, name);
        List<string> args =
        [
            "-hide_banner", "-y", "-f", "lavfi", "-i", "color=black:s=320x180:d=1:r=25,format=yuv420p10le,geq=lum='if(lt(X,W/2),420,95)':cb=512:cr=512",
            "-c:v", "libx264", "-pix_fmt", "yuv420p10le", "-color_range", "tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
            "-metadata", "creation_time=" + Recorded.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        ];
        if (sony)
            args.AddRange(["-metadata", "comment=<Item name=\"CaptureGammaEquation\" value=\"s-log3-cine\"/><Item name=\"CaptureColorPrimaries\" value=\"S-Gamut3-Cine\"/>"]);
        args.Add(path);
        ProcessRunner.RunAsync(_tools.Ffmpeg!, args, default).GetAwaiter().GetResult();
        File.SetLastWriteTimeUtc(path, Recorded);
        return path;
    }

    private byte[] Pixels(string video, int x, int y)
    {
        var raw = Path.Combine(_dir, Path.GetFileNameWithoutExtension(video) + ".rgb");
        ProcessRunner.RunAsync(_tools.Ffmpeg!, ["-hide_banner", "-y", "-i", video, "-vf", $"crop=8:8:{x}:{y}", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", raw], default)
            .GetAwaiter().GetResult();
        return File.ReadAllBytes(raw);
    }

    private async Task<JsonElement> Probe(string video)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(_tools.Ffmpeg!)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var json = await ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", video], default);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Sony_SLog3_clip_becomes_normal_video_with_its_date()
    {
        if (!_tools.Has(ExternalTool.Ffmpeg))
            return;
        var clip = MakeClip("C0001.MP4", sony: true);
        var plain = MakeClip("plain.mp4", sony: false);
        var op = Catalog.Build().Single(o => o.Id == "video.slog3");
        Assert.True(op.AppliesTo([new FileItem(clip)]));
        Assert.False(op.AppliesTo([new FileItem(plain)]));

        var output = Assert.Single((await op.ExecuteAsync([new FileItem(clip)], Context("slog"))).Outputs);
        Assert.Equal("C0001 (Rec.709).mp4", Path.GetFileName(output));
        Assert.Equal(Recorded, File.GetLastWriteTimeUtc(output));

        var probe = await Probe(output);
        var stream = probe.GetProperty("streams")[0];
        Assert.Equal("h264", stream.GetProperty("codec_name").GetString());
        Assert.Equal("yuv420p", stream.GetProperty("pix_fmt").GetString());
        Assert.StartsWith("2024-05-01T10:00:00", probe.GetProperty("format").GetProperty("tags").GetProperty("creation_time").GetString());

        // Middle grey comes out at about 42 % (107 of 255), neutral; log black goes to black.
        var grey = Pixels(output, 76, 86);
        Assert.InRange(grey[0], 95, 120);
        Assert.InRange(Math.Abs(grey[0] - grey[1]), 0, 4);
        Assert.InRange(Math.Abs(grey[0] - grey[2]), 0, 4);
        Assert.InRange(Pixels(output, 236, 86)[0], 0, 12);
    }

    [Fact]
    public async Task A_cube_dropped_with_videos_is_applied_to_each()
    {
        if (!_tools.Has(ExternalTool.Ffmpeg))
            return;
        var a = MakeClip("a.mp4", sony: false);
        var b = MakeClip("b.mov", sony: false);
        // An inverting 2×2×2 LUT: easy to recognise in the result.
        var cube = Path.Combine(_dir, "Negativ, test.cube");
        File.WriteAllText(cube, "LUT_3D_SIZE 2\n1 1 1\n0 1 1\n1 0 1\n0 0 1\n1 1 0\n0 1 0\n1 0 0\n0 0 0\n");
        var op = Catalog.Build().Single(o => o.Id == "video.lut");
        Assert.False(op.AppliesTo([new FileItem(a)]));
        Assert.False(op.AppliesTo([new FileItem(cube)]));
        Assert.True(op.AppliesTo([new FileItem(a), new FileItem(b), new FileItem(cube)]));

        var outputs = (await op.ExecuteAsync([new FileItem(a), new FileItem(cube), new FileItem(b)], Context("lut"))).Outputs;
        Assert.Equal(["a (Negativ, test).mp4", "b (Negativ, test).mp4"], outputs.Select(o => Path.GetFileName(o)!).Order().ToArray());
        // Black (right half) turns white.
        Assert.InRange(Pixels(outputs[0], 236, 86)[0], 230, 255);
        Assert.Equal(Recorded, File.GetLastWriteTimeUtc(outputs[0]));
    }
}
