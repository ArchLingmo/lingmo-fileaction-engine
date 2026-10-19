using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FileAction.Core;
using FileAction.Core.Backends;

namespace FileAction.Tests;

public sealed class ArchiveQualityTests : IDisposable
{
    private readonly string _dir = OutputPaths.TempDirectory();

    public void Dispose() => OutputPaths.TryDelete(_dir);

    private string Write(string relative, string text = "obsah")
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static OperationContext Context(string outDir) => new() { Tools = new ToolLocator(), OutputDirectory = outDir };

    [Fact]
    public async Task Explorer_zip_keeps_czech_names()
    {
        // Windows Explorer writes names in the DOS code page (CP852 here) without the UTF-8 flag.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var zip = Path.Combine(_dir, "staré.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create, Encoding.GetEncoding(852)))
        {
            using (var w = new StreamWriter(archive.CreateEntry("Složka čeština/Příliš žluťoučký kůň.txt").Open()))
                w.Write("ahoj");
            using (var w = new StreamWriter(archive.CreateEntry("Složka čeština/šťáva.txt").Open()))
                w.Write("ahoj");
        }
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ");
        try
        {
            var outDir = Path.Combine(_dir, "out");
            var folder = await ArchiveBackend.ExtractAsync(zip, outDir, default);
            Assert.Equal("Složka čeština", Path.GetFileName(folder));
            Assert.True(File.Exists(Path.Combine(folder, "Příliš žluťoučký kůň.txt")));
            Assert.True(File.Exists(Path.Combine(folder, "šťáva.txt")));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Tar_packs_read_only_and_hidden_files_and_empty_folders()
    {
        var root = Path.Combine(_dir, "Moje složka");
        var ro = Write("Moje složka/jen čtení.txt");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        var hidden = Write("Moje složka/skrytý.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(root, "prázdná"));
        try
        {
            var tar = Path.Combine(_dir, "a.tar");
            await ArchiveBackend.PackAsync([root], tar, "tar", default);
            var names = new List<string>();
            await using (var stream = File.OpenRead(tar))
            {
                using var reader = new TarReader(stream);
                while (reader.GetNextEntry() is { } entry)
                    names.Add(entry.Name);
            }
            Assert.Contains("Moje složka/jen čtení.txt", names);
            Assert.Contains("Moje složka/skrytý.txt", names);
            Assert.Contains("Moje složka/prázdná/", names);

            var zip = Path.Combine(_dir, "a.zip");
            await ArchiveBackend.PackAsync([root], zip, "zip", default);
            using var archive = ZipFile.OpenRead(zip);
            Assert.Contains(archive.Entries, e => e.FullName == "Moje složka/prázdná/");
        }
        finally
        {
            File.SetAttributes(ro, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Repack_keeps_the_top_folder()
    {
        var zip = Path.Combine(_dir, "proj.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("proj/readme.txt");
            archive.CreateEntry("proj/sub/y.txt");
        }
        var outDir = Path.Combine(_dir, "out");
        var tgz = await ArchiveBackend.RepackAsync(new FileItem(zip), "tar.gz", Context(outDir));
        var names = new List<string>();
        await using (var file = File.OpenRead(tgz))
        await using (var gz = new GZipStream(file, CompressionMode.Decompress))
        {
            using var reader = new TarReader(gz);
            while (reader.GetNextEntry() is { } entry)
                names.Add(entry.Name);
        }
        Assert.Contains("proj/readme.txt", names);
        Assert.Contains("proj/sub/y.txt", names);
    }

    [Fact]
    public async Task Zip_with_a_tar_inside_extracts_only_once()
    {
        var tar = Write("backup.tar", "");
        File.Delete(tar);
        await using (var stream = File.Create(tar))
        {
            using var writer = new TarWriter(stream);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "inside.txt") { DataStream = new MemoryStream("x"u8.ToArray()) };
            writer.WriteEntry(entry);
        }
        var zip = Path.Combine(_dir, "wrapper.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(tar, "backup.tar");
        var result = await ArchiveBackend.ExtractAsync(zip, Path.Combine(_dir, "out"), default);
        Assert.Equal("backup.tar", Path.GetFileName(result));
    }

    [Fact]
    public async Task Names_windows_cannot_store_are_cleaned_not_lost()
    {
        // On Linux these names are valid and are kept as they are.
        if (!OperatingSystem.IsWindows())
            return;
        var tar = Path.Combine(_dir, "linux.tar");
        await using (var stream = File.Create(tar))
        {
            using var writer = new TarWriter(stream);
            foreach (var name in new[] { "dir/a:b.txt", "dir/what?.txt", "dir/trailing." })
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream("x"u8.ToArray()) });
        }
        var folder = await ArchiveBackend.ExtractAsync(tar, Path.Combine(_dir, "out"), default);
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["a_b.txt", "trailing", "what_.txt"], files);
        Assert.All(Directory.GetFiles(folder), f => Assert.Equal(1, new FileInfo(f).Length));
    }

    [Fact]
    public void Zip_slip_is_still_blocked() =>
        Assert.Throws<ConversionException>(() => ArchiveBackend.SafeTarget(Path.GetTempPath(), "../../evil.exe"));
}

public sealed class ArchiveProgressTests : IDisposable
{
    private readonly string _dir = OutputPaths.TempDirectory();

    public void Dispose() => OutputPaths.TryDelete(_dir);

    private sealed class Recorder : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value)
        {
            lock (Values)
                Values.Add(value);
        }
    }

    private string BigFolder()
    {
        var folder = Path.Combine(_dir, "data");
        Directory.CreateDirectory(folder);
        var bytes = new byte[8 * 1024 * 1024];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(folder, "a.bin"), bytes);
        File.WriteAllBytes(Path.Combine(folder, "b.bin"), bytes);
        return folder;
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar.gz")]
    public async Task Pack_and_extract_report_progress(string format)
    {
        var folder = BigFolder();
        var archive = Path.Combine(_dir, "data." + format);
        var packing = new Recorder();
        await ArchiveBackend.PackAsync([folder], archive, format, default, packing);
        Assert.True(packing.Values.Count > 10, $"only {packing.Values.Count} progress reports");
        Assert.Equal(1, packing.Values.Max(), 3);
        Assert.Equal(packing.Values.OrderBy(v => v), packing.Values);

        var extracting = new Recorder();
        var result = await ArchiveBackend.ExtractAsync(archive, Path.Combine(_dir, "out"), default, extracting);
        Assert.True(extracting.Values.Count > 5);
        Assert.True(extracting.Values.Max() > 0.9);
        Assert.Equal(16 * 1024 * 1024, Directory.GetFiles(result).Sum(f => new FileInfo(f).Length));
    }

    [Fact]
    public async Task Cancel_stops_inside_a_large_file()
    {
        var folder = BigFolder();
        using var cts = new CancellationTokenSource();
        var progress = new Progress<double>();
        var reports = 0;
        var recorder = new CancelAfter(() => { if (++reports == 3) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArchiveBackend.PackAsync([folder], Path.Combine(_dir, "x.zip"), "zip", cts.Token, recorder));
    }

    private sealed class CancelAfter(Action onReport) : IProgress<double>
    {
        public void Report(double value) => onReport();
    }
}

public sealed class PdfMergeTests : IDisposable
{
    private readonly string _dir = OutputPaths.TempDirectory();

    public void Dispose() => OutputPaths.TryDelete(_dir);

    private string Pdf(string name, int pages, string? bookmark)
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        for (var i = 0; i < pages; i++)
            doc.AddPage();
        if (bookmark is not null)
            doc.Outlines.Add(bookmark, doc.Pages[pages - 1], true);
        var path = Path.Combine(_dir, name);
        doc.Save(path);
        return path;
    }

    [Fact]
    public void Merge_keeps_bookmarks_and_adds_one_per_file()
    {
        var first = Pdf("Smlouva.pdf", 2, "Podpisy");
        var second = Pdf("Příloha.pdf", 1, null);
        var merged = PdfBackend.Merge([first, second], Path.Combine(_dir, "merged.pdf"));
        using var result = PdfSharp.Pdf.IO.PdfReader.Open(merged, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(3, result.PageCount);
        var top = result.Outlines.Select(o => o.Title).ToList();
        Assert.Equal(["Smlouva", "Příloha"], top);
        var inner = Assert.Single(result.Outlines[0].Outlines);
        Assert.Equal("Podpisy", inner.Title);
        int Index(PdfSharp.Pdf.PdfPage page) => result.Pages.Cast<PdfSharp.Pdf.PdfPage>().ToList().FindIndex(p => p.Reference?.ObjectID == page.Reference?.ObjectID);
        Assert.Equal(1, Index(inner.DestinationPage!));
        Assert.Equal(2, Index(result.Outlines[1].DestinationPage!));
    }
}
