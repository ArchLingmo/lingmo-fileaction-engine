using static FileAction.Core.Loc;

namespace FileAction.Core;

public static class OutputPaths
{
    /// <summary>"dir/name.ext", or "dir/name (2).ext" … if that already exists.</summary>
    public static string Unique(string dir, string baseName, string ext)
    {
        ext = ext.TrimStart('.');
        var suffix = ext.Length == 0 ? "" : "." + ext;
        var candidate = Path.Combine(dir, baseName + suffix);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{baseName} ({i}){suffix}");
        return candidate;
    }

    public static string UniqueDirectory(string dir, string name) => Unique(dir, name, "");

    /// <summary>
    /// Runs <paramref name="produce"/> against a hidden temporary path in <paramref name="dir"/> and,
    /// when it succeeds, moves the file to a unique final name. Partial output is removed on failure.
    /// </summary>
    public static async Task<string> ProduceAsync(string dir, string baseName, string ext, Func<string, Task> produce)
    {
        Directory.CreateDirectory(dir);
        // Short on purpose: a long base name plus a suffix could exceed the 255-character name limit.
        var temp = Path.Combine(dir, $".fa-{Guid.NewGuid().ToString("N")[..8]}.part.{ext}");
        try
        {
            await produce(temp).ConfigureAwait(false);
            if (!File.Exists(temp))
                throw new ConversionException(T("Převod nevytvořil žádný výstup.", "The conversion produced no output."));
            var final = Unique(dir, baseName, ext);
            File.Move(temp, final);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Like <see cref="ProduceAsync"/>, for a producer that may decline (returns null and leaves nothing behind
    /// when it wrote no file).
    /// </summary>
    public static async Task<string?> ProduceOptionalAsync(string dir, string baseName, string ext, Func<string, Task> produce)
    {
        Directory.CreateDirectory(dir);
        // Short on purpose: a long base name plus a suffix could exceed the 255-character name limit.
        var temp = Path.Combine(dir, $".fa-{Guid.NewGuid().ToString("N")[..8]}.part.{ext}");
        try
        {
            await produce(temp).ConfigureAwait(false);
            if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
                return null;
            var final = Unique(dir, baseName, ext);
            File.Move(temp, final);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// A folder of results (PDF pages, rendered pages): filled under a hidden temporary name and moved into
    /// place only when complete, so nobody (a watched folder, the user) sees a half-written folder.
    /// </summary>
    public static async Task<string> ProduceDirectoryAsync(string dir, string name, Func<string, Task> produce)
    {
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".fa-{Guid.NewGuid().ToString("N")[..8]}.part");
        Directory.CreateDirectory(temp);
        try
        {
            await produce(temp).ConfigureAwait(false);
            var final = UniqueDirectory(dir, name);
            Directory.Move(temp, final);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileAction", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <param name="nothingToDo">
/// Not a failure: the file is already what the operation would make (already small enough, already 720p).
/// Shown as a neutral note, not as an error.
/// </param>
public sealed class ConversionException(string message, Exception? inner = null, bool nothingToDo = false) : Exception(message, inner)
{
    public bool NothingToDo { get; } = nothingToDo;
}
