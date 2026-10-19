using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>Extracting (ZIP, TAR, GZ, BZ2, XZ, RAR, 7z) and packing (ZIP, TAR, TAR.GZ).</summary>
public static class ArchiveBackend
{
    private static readonly ExtractionOptions Options = new() { ExtractFullPath = true, Overwrite = false, PreserveFileTime = true };

    /// <summary>Compressed single streams that may hold a .tar (backup.tar.gz read as gzip).</summary>
    private static readonly HashSet<string> Wrappers = new(StringComparer.OrdinalIgnoreCase) { "gz", "bz2", "xz", "tar.gz", "tar.bz2", "tar.xz" };

    static ArchiveBackend() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// ZIPs from Windows Explorer store names in the DOS code page of the PC (CP852 in Czech) without the
    /// UTF-8 flag. Entries that do have the flag are still read as UTF-8.
    /// </summary>
    private static ReaderOptions Reader() => new()
    {
        ArchiveEncoding = new ArchiveEncoding { Default = LegacyEncoding() },
    };

    private static Encoding LegacyEncoding()
    {
        try
        {
            var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oem is 0 or 1 or 65001 ? 437 : oem);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.GetEncoding(437);
        }
    }

    /// <summary>
    /// Extracts next to the archive. When the archive holds exactly one top-level item it is placed
    /// directly in <paramref name="destinationDir"/>; otherwise everything goes into a folder named after the archive.
    /// </summary>
    public static async Task<string> ExtractAsync(string archive, string destinationDir, CancellationToken ct)
    {
        var temp = Path.Combine(destinationDir, $".fa-{Guid.NewGuid().ToString("N")[..8]}.part");
        Directory.CreateDirectory(temp);
        try
        {
            await Task.Run(() => ExtractAll(archive, temp, ct), ct).ConfigureAwait(false);

            var entries = Directory.GetFileSystemEntries(temp);
            if (entries.Length == 0)
                throw new ConversionException(T("Archiv je prázdný.", "The archive is empty."));
            if (entries.Length == 1)
            {
                var name = Path.GetFileName(entries[0]);
                var isDir = Directory.Exists(entries[0]);
                var target = isDir
                    ? OutputPaths.UniqueDirectory(destinationDir, name)
                    : OutputPaths.Unique(destinationDir, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name).TrimStart('.'));
                MoveEntry(entries[0], target);
                return target;
            }
            var folder = OutputPaths.UniqueDirectory(destinationDir, Formats.BaseNameOf(archive));
            Directory.Move(temp, folder);
            return folder;
        }
        finally
        {
            OutputPaths.TryDelete(temp);
        }
    }

    /// <summary>Everything in the archive into <paramref name="destination"/>; a .tar inside a .gz/.bz2/.xz is unpacked too.</summary>
    private static void ExtractAll(string archive, string destination, CancellationToken ct)
    {
        ExtractTo(archive, destination, ct);
        if (!Wrappers.Contains(Formats.KeyOf(archive)))
            return;
        var only = Directory.GetFileSystemEntries(destination);
        if (only.Length != 1 || !File.Exists(only[0]) || Formats.KeyOf(only[0]) != "tar")
            return;
        var inner = only[0];
        var innerDir = Path.Combine(destination, ".inner");
        Directory.CreateDirectory(innerDir);
        ExtractTo(inner, innerDir, ct);
        File.Delete(inner);
        foreach (var e in Directory.GetFileSystemEntries(innerDir))
            MoveEntry(e, Path.Combine(destination, Path.GetFileName(e)));
        Directory.Delete(innerDir);
    }

    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    private static void ExtractTo(string archive, string destination, CancellationToken ct)
    {
        var fullDestination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        try
        {
            if (Formats.KeyOf(archive) == "7z")
            {
                using var a = ArchiveFactory.OpenArchive(archive, Reader());
                foreach (var entry in a.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Key is null)
                        continue;
                    var path = SafeTarget(fullDestination, entry.Key);
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(path);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    entry.WriteToFile(Free(path), Options);
                }
                return;
            }

            using var stream = File.OpenRead(archive);
            using var reader = ReaderFactory.OpenReader(stream, Reader());
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key))
                    key = Formats.BaseNameOf(archive); // single-file .gz/.bz2/.xz
                var path = SafeTarget(fullDestination, key);
                if (entry.IsDirectory)
                {
                    // Empty folders are part of what the archive holds.
                    Directory.CreateDirectory(path);
                    continue;
                }
                if (!string.IsNullOrEmpty(entry.LinkTarget))
                    continue; // symbolic and hard links: not portable to Windows, and a link could point outside
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                reader.WriteEntryToFile(Free(path), Options);
            }
        }
        catch (Exception ex) when (ex is InvalidFormatException or ArchiveOperationException or CryptographicException
                                       or ExtractionException or ArchiveException or IncompleteArchiveException)
        {
            throw new ConversionException(T($"Archiv nelze rozbalit: {ex.Message}", $"Cannot extract the archive: {ex.Message}"), ex);
        }
    }

    /// <summary>
    /// Two entries can end up with the same name after cleaning (e.g. "a:b" and "a_b"); never overwrite,
    /// number the second one instead.
    /// </summary>
    private static string Free(string path) =>
        File.Exists(path) ? OutputPaths.Unique(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path).TrimStart('.')) : path;

    /// <summary>
    /// Rejects entries like "../../evil.exe" that would escape the target folder, and makes every path segment
    /// a valid file name here (archives from Linux may contain "a:b.txt" or "what?.txt").
    /// </summary>
    public static string SafeTarget(string fullDestination, string entryKey)
    {
        var segments = entryKey.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleaned = segments.Select(s => s is "." or ".." ? s : CleanSegment(s));
        var path = Path.GetFullPath(Path.Combine(fullDestination, string.Join(Path.DirectorySeparatorChar, cleaned)));
        if (!path.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
            throw new ConversionException(T($"Archiv obsahuje nebezpečnou cestu: {entryKey}", $"The archive contains an unsafe path: {entryKey}"));
        return path;
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string CleanSegment(string segment)
    {
        if (!OperatingSystem.IsWindows())
            return segment;
        var sb = new StringBuilder(segment.Length);
        foreach (var c in segment)
            sb.Append(c < 32 || c is '<' or '>' or ':' or '"' or '|' or '?' or '*' ? '_' : c);
        var name = sb.ToString().TrimEnd('.', ' ');
        if (name.Length == 0)
            name = "_";
        var stem = name.Split('.')[0];
        return ReservedNames.Contains(stem) ? "_" + name : name;
    }

    /// <summary>Packs files and folders into one archive ("zip", "tar" or "tar.gz"), empty folders included.</summary>
    public static async Task PackAsync(IReadOnlyList<string> items, string output, string format, CancellationToken ct)
    {
        var entries = new List<(string Source, string Name)>();
        foreach (var item in items)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(item);
            if (Directory.Exists(trimmed))
            {
                var root = Path.GetFileName(trimmed);
                if (root.Length == 0)
                    root = T("Disk", "Drive") + " " + trimmed.TrimEnd(':', '\\', '/');
                if (!Directory.EnumerateFileSystemEntries(trimmed).Any())
                    entries.Add((trimmed, root + "/"));
                foreach (var dir in Directory.EnumerateDirectories(trimmed, "*", SearchOption.AllDirectories))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        entries.Add((dir, root + "/" + Path.GetRelativePath(trimmed, dir).Replace('\\', '/') + "/"));
                }
                foreach (var file in Directory.EnumerateFiles(trimmed, "*", SearchOption.AllDirectories))
                    entries.Add((file, root + "/" + Path.GetRelativePath(trimmed, file).Replace('\\', '/')));
            }
            else
            {
                entries.Add((trimmed, Path.GetFileName(trimmed)));
            }
        }

        await Task.Run(async () =>
        {
            await using var file = File.Create(output);
            if (format == "zip")
            {
                using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                foreach (var (source, name) in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (name.EndsWith('/'))
                        zip.CreateEntry(name);
                    else
                        zip.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
                }
                return;
            }

            Stream target = format == "tar.gz" ? new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true) : file;
            try
            {
                await using var tar = new TarWriter(target, TarEntryFormat.Pax, leaveOpen: true);
                foreach (var (source, name) in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    // Entries are built by hand: TarWriter's path overload refuses read-only and hidden files
                    // ("file type not supported"), which are everyday on Windows.
                    if (name.EndsWith('/'))
                    {
                        await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, name) { ModificationTime = Directory.GetLastWriteTimeUtc(source) }, ct)
                            .ConfigureAwait(false);
                        continue;
                    }
                    await using var data = File.OpenRead(source);
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                    {
                        DataStream = data,
                        ModificationTime = File.GetLastWriteTimeUtc(source),
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    };
                    await tar.WriteEntryAsync(entry, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                if (target != file)
                    await target.DisposeAsync().ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
    }

    public static string ArchiveNameFor(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && items[0].BaseName.Length > 0 ? items[0].BaseName : T("Archiv", "Archive");

    /// <summary>Repacks an archive into another archive format, with exactly the same contents and folders.</summary>
    public static async Task<string> RepackAsync(FileItem item, string format, OperationContext ctx)
    {
        var temp = OutputPaths.TempDirectory();
        try
        {
            await Task.Run(() => ExtractAll(item.Path, temp, ctx.CancellationToken), ctx.CancellationToken).ConfigureAwait(false);
            var contents = Directory.GetFileSystemEntries(temp);
            if (contents.Length == 0)
                throw new ConversionException(T("Archiv je prázdný.", "The archive is empty."));
            return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
                PackAsync(contents, tmp, format, ctx.CancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(temp);
        }
    }
}
