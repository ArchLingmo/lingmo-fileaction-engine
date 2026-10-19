using FileAction.Core;
using ImageMagick;

namespace FileAction.Tests;

public class PresetTests
{
    [Fact]
    public void Format_changes_go_on_convert_and_same_format_on_tools()
    {
        var convert = PresetOperations.Build(new Preset { Name = "Web", Kind = PresetKind.Image, Format = "jpg" });
        var same = PresetOperations.Build(new Preset { Name = "Půlka", Kind = PresetKind.Image, Format = Preset.SameFormat, Percent = 50 });
        Assert.Equal(WheelKind.Convert, convert.Wheel);
        Assert.Equal(WheelKind.Tools, same.Wheel);
        Assert.StartsWith("preset.", convert.Id);
    }

    [Fact]
    public void Operation_fits_its_file_types()
    {
        var image = PresetOperations.Build(new Preset { Kind = PresetKind.Image, Format = "webp" });
        var audio = PresetOperations.Build(new Preset { Kind = PresetKind.Audio, Format = "mp3" });
        Assert.True(image.AppliesTo([new FileItem("a.png")]));
        Assert.False(image.AppliesTo([new FileItem("a.mp4")]));
        // Audio presets also take the sound out of a video.
        Assert.True(audio.AppliesTo([new FileItem("a.mp4")]));
        Assert.Contains(ExternalTool.Ffmpeg, audio.Requires);
    }

    [Fact]
    public void Summary_lists_what_matters()
    {
        Assert.Equal("JPG · kvalita 70 · max 1920 px · bez metadat",
            PresetOperations.Summary(new Preset { Kind = PresetKind.Image, Format = "jpg", Quality = 70, MaxSide = 1920, StripMetadata = true }));
        Assert.Equal("PNG · 50 %", PresetOperations.Summary(new Preset { Kind = PresetKind.Image, Format = "png", Percent = 50 }));
        Assert.Equal("MP4 · 720p · malý soubor · bez zvuku",
            PresetOperations.Summary(new Preset { Kind = PresetKind.Video, Format = "mp4", Height = 720, VideoQuality = VideoQuality.Small, Mute = true }));
        Assert.Equal("FLAC", PresetOperations.Summary(new Preset { Kind = PresetKind.Audio, Format = "flac" }));
    }

    [Theory]
    [InlineData("Web 1920", "Web 1920")]
    [InlineData("a/b:c*?", "a-b-c--")]
    [InlineData("  ...  ", "preset")]
    public void Name_is_safe_in_a_file_name(string name, string expected) => Assert.Equal(expected, PresetOperations.SafeSuffix(name));

    [Fact]
    public async Task Image_preset_resizes_and_converts()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = Path.Combine(dir, "photo.png");
            using (var image = new MagickImage(MagickColors.Orange, 400, 200))
                image.Write(source, MagickFormat.Png);
            var op = PresetOperations.Build(new Preset { Name = "Malé", Kind = PresetKind.Image, Format = "jpg", Quality = 70, MaxSide = 100 });
            var result = await op.ExecuteAsync([new FileItem(source)], new OperationContext { Tools = new ToolLocator() });
            var output = Assert.Single(result.Outputs);
            Assert.Equal("photo (Malé).jpg", Path.GetFileName(output));
            using var written = new MagickImage(output);
            Assert.Equal(MagickFormat.Jpeg, written.Format);
            Assert.Equal(100u, written.Width);
            Assert.Equal(50u, written.Height);
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }
}

public class WatchRuleTests
{
    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("report.pdf", true)]
    [InlineData("video.mp4.crdownload", false)]
    [InlineData("big.zip.part", false)]
    [InlineData("~$report.docx", false)]
    [InlineData(".hidden.png", false)]
    [InlineData(".photo.1a2b3c4d.part.webp", false)]
    [InlineData("desktop.ini", false)]
    public void Skips_unfinished_and_system_files(string name, bool expected) =>
        Assert.Equal(expected, WatchRules.IsCandidate(Path.Combine(Path.GetTempPath(), name)));

    [Fact]
    public void Subfolders_only_when_allowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "watched");
        var direct = Path.Combine(root, "a.png");
        var nested = Path.Combine(root, "sub", "a.png");
        var sibling = Path.Combine(Path.GetTempPath(), "watched-other", "a.png");
        var folder = new WatchFolder { Path = root };
        Assert.True(WatchRules.IsInside(folder, direct));
        Assert.False(WatchRules.IsInside(folder, nested));
        Assert.False(WatchRules.IsInside(folder, sibling));
        folder.IncludeSubfolders = true;
        Assert.True(WatchRules.IsInside(folder, nested));
        Assert.False(WatchRules.IsInside(folder, sibling));
    }
}

public class WheelOrderTests
{
    [Fact]
    public void Arranged_slices_come_first_and_keep_the_rest()
    {
        Assert.Equal(["b", "a", "x", "y"], WheelOrder.Merge(["b", "a"], ["x", "a", "y"]));
    }

    [Theory]
    [InlineData("c", 0, "c,a,b")]
    [InlineData("a", 2, "b,c,a")]
    [InlineData("new", 1, "a,new,b,c")]
    [InlineData("b", 99, "a,c,b")]
    public void Move_to_inserts_at_the_index(string id, int index, string expected) =>
        Assert.Equal(expected.Split(','), WheelOrder.MoveTo(["a", "b", "c"], id, index));

    [Fact]
    public void Relocated_operation_runs_like_the_original()
    {
        var compress = FileAction.Core.Operations.Catalog.Build().Single(o => o.Id == "image.compress");
        var ops = RelocatedOperation.Apply([compress], new Dictionary<string, WheelKind> { ["image.compress"] = WheelKind.Convert });
        var moved = Assert.IsType<RelocatedOperation>(Assert.Single(ops));
        Assert.Equal(WheelKind.Convert, moved.Wheel);
        Assert.Equal(WheelKind.Tools, moved.HomeWheel);
        Assert.Equal(compress.Label, moved.Label);
        Assert.True(moved.AppliesTo([new FileItem("a.jpg")]));
        Assert.Same(compress, RelocatedOperation.Unwrap(moved));
        // Moving to the home wheel changes nothing.
        Assert.Same(compress, RelocatedOperation.Apply([compress], new Dictionary<string, WheelKind> { ["image.compress"] = WheelKind.Tools })[0]);
    }
}
