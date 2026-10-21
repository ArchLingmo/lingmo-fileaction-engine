using System.Globalization;
using System.Text;

namespace FileAction.Core.Backends;

/// <summary>
/// Log footage (Sony S-Log3) to normal Rec.709 video, and any .cube LUT applied to a video. The built-in LUT is
/// computed from Sony's published S-Log3 curve and S-Gamut3 / S-Gamut3.Cine primaries, so no third-party LUT
/// file is shipped.
/// </summary>
public static class LogVideo
{
    public enum Gamut { SGamut3Cine, SGamut3 }

    /// <summary>What a Sony clip says about itself, or null when it is not S-Log3.</summary>
    public sealed record LogProfile(Gamut Gamut);

    private static readonly Dictionary<(string, DateTime, long), LogProfile?> DetectCache = new();

    /// <summary>
    /// Sony cameras write an XML block (NonRealTimeMeta) into the MP4 with the picture profile's gamma, e.g.
    /// <c>CaptureGammaEquation value="s-log3-cine"</c>. It sits near the start or in the index at the end, so
    /// only those parts of the file are read.
    /// </summary>
    public static LogProfile? Detect(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var key = (info.FullName, info.LastWriteTimeUtc, info.Length);
            lock (DetectCache)
            {
                if (DetectCache.TryGetValue(key, out var cached))
                    return cached;
            }
            var text = ReadEnds(path, info.Length).ToLowerInvariant();
            LogProfile? profile = null;
            var gamma = text.IndexOf("capturegammaequation", StringComparison.Ordinal);
            if (gamma >= 0 && text.AsSpan(gamma, Math.Min(120, text.Length - gamma)).Contains("s-log3", StringComparison.Ordinal))
            {
                var primaries = text.IndexOf("capturecolorprimaries", StringComparison.Ordinal);
                var gamut = primaries >= 0 ? text.Substring(primaries, Math.Min(120, text.Length - primaries)) : "";
                profile = new LogProfile(gamut.Contains("s-gamut3", StringComparison.Ordinal) && !gamut.Contains("cine", StringComparison.Ordinal)
                    ? Gamut.SGamut3
                    : Gamut.SGamut3Cine);
            }
            lock (DetectCache)
                DetectCache[key] = profile;
            return profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadEnds(string path, long length)
    {
        const int Head = 8 << 20, Tail = 16 << 20;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var sb = new StringBuilder();
        var buffer = new byte[(int)Math.Min(length, Head)];
        stream.ReadExactly(buffer);
        sb.Append(Encoding.Latin1.GetString(buffer));
        if (length > Head)
        {
            var tail = (int)Math.Min(length - Head, Tail);
            stream.Seek(length - tail, SeekOrigin.Begin);
            buffer = new byte[tail];
            stream.ReadExactly(buffer);
            sb.Append(Encoding.Latin1.GetString(buffer));
        }
        return sb.ToString();
    }

    /// <summary>S-Log3 code value (0–1 of 1023) to scene-linear reflection (0.18 = middle grey).</summary>
    public static double SLog3ToLinear(double y)
    {
        var cv = y * 1023;
        return cv >= 171.2102946929
            ? Math.Pow(10, (cv - 420) / 261.5) * (0.18 + 0.01) - 0.01
            : (cv - 95) * 0.01125000 / (171.2102946929 - 95);
    }

    /// <summary>Scene-linear reflection to S-Log3 code value (0–1 of 1023).</summary>
    public static double LinearToSLog3(double x) =>
        x >= 0.01125000
            ? (420 + Math.Log10((x + 0.01) / (0.18 + 0.01)) * 261.5) / 1023
            : (x * (171.2102946929 - 95) / 0.01125000 + 95) / 1023;

    /// <summary>Linear S-Gamut3(.Cine) RGB to linear Rec.709 RGB, both with a D65 white.</summary>
    public static double[,] ToRec709(Gamut gamut)
    {
        var source = gamut == Gamut.SGamut3Cine
            ? Primaries((0.766, 0.275), (0.225, 0.800), (0.089, -0.087))
            : Primaries((0.730, 0.280), (0.140, 0.855), (0.100, -0.050));
        var rec709 = Primaries((0.640, 0.330), (0.300, 0.600), (0.150, 0.060));
        return Multiply(Invert(rec709), source);
    }

    /// <summary>
    /// The look: exposure set so middle grey lands where Sony's own LC-709 puts it (about 42 % of the signal),
    /// a filmic curve that rolls the log highlights off instead of clipping them, then the Rec.709 signal curve.
    /// </summary>
    public static (double R, double G, double B) Grade(double r, double g, double b, double[,] matrix)
    {
        var lr = SLog3ToLinear(r);
        var lg = SLog3ToLinear(g);
        var lb = SLog3ToLinear(b);
        var or = matrix[0, 0] * lr + matrix[0, 1] * lg + matrix[0, 2] * lb;
        var og = matrix[1, 0] * lr + matrix[1, 1] * lg + matrix[1, 2] * lb;
        var ob = matrix[2, 0] * lr + matrix[2, 1] * lg + matrix[2, 2] * lb;
        return (Display(or), Display(og), Display(ob));

        static double Display(double linear)
        {
            const double Exposure = 0.553;
            var x = Math.Max(0, linear) * Exposure;
            // Narkowicz's fit of the ACES film curve: soft toe, long shoulder.
            var film = Math.Clamp(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14), 0, 1);
            return Math.Pow(film, 1 / 2.4);
        }
    }

    /// <summary>
    /// The 3D LUT for ffmpeg's lut3d. Its input is the video's full-range RGB, which for Sony's legal-range
    /// recordings is (code − 64) / 876, while the S-Log3 curve is defined on the code value over 1023.
    /// </summary>
    public static string SLog3Cube(Gamut gamut, int size = 33)
    {
        var matrix = ToRec709(gamut);
        var sb = new StringBuilder();
        sb.AppendLine("TITLE \"FileAction S-Log3 to Rec.709\"");
        sb.AppendLine($"LUT_3D_SIZE {size}");
        for (var bi = 0; bi < size; bi++)
        for (var gi = 0; gi < size; gi++)
        for (var ri = 0; ri < size; ri++)
        {
            static double Code(int i, int size) => (64 + i / (double)(size - 1) * 876) / 1023;
            var (r, g, b) = Grade(Code(ri, size), Code(gi, size), Code(bi, size), matrix);
            sb.Append(r.ToString("0.000000", CultureInfo.InvariantCulture)).Append(' ')
              .Append(g.ToString("0.000000", CultureInfo.InvariantCulture)).Append(' ')
              .Append(b.ToString("0.000000", CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Whether a .cube file is a 1D LUT (lut1d) rather than a 3D one (lut3d).</summary>
    public static bool IsOneDimensional(string cube)
    {
        foreach (var line in File.ReadLines(cube).Take(200))
        {
            var t = line.TrimStart();
            if (t.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                return true;
            if (t.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        throw new ConversionException(Loc.T("Soubor .cube neobsahuje LUT (chybí LUT_3D_SIZE).", "The .cube file holds no LUT (LUT_3D_SIZE is missing)."));
    }

    /// <summary>A path quoted for an ffmpeg filter option (Windows drive colons and backslashes escaped).</summary>
    public static string FilterPath(string path) =>
        "'" + path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\''") + "'";

    /// <summary>Keeps a converted copy where it belongs in a photo library: the source file's dates.</summary>
    public static void CopyFileDates(string source, string output)
    {
        try
        {
            File.SetCreationTimeUtc(output, File.GetCreationTimeUtc(source));
            File.SetLastWriteTimeUtc(output, File.GetLastWriteTimeUtc(source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }
    }

    private static double[,] Primaries((double X, double Y) r, (double X, double Y) g, (double X, double Y) b)
    {
        (double X, double Y) white = (0.3127, 0.3290);
        double[,] m =
        {
            { r.X / r.Y, g.X / g.Y, b.X / b.Y },
            { 1, 1, 1 },
            { (1 - r.X - r.Y) / r.Y, (1 - g.X - g.Y) / g.Y, (1 - b.X - b.Y) / b.Y },
        };
        double[] w = [white.X / white.Y, 1, (1 - white.X - white.Y) / white.Y];
        var inv = Invert(m);
        var s = new double[3];
        for (var i = 0; i < 3; i++)
            s[i] = inv[i, 0] * w[0] + inv[i, 1] * w[1] + inv[i, 2] * w[2];
        var result = new double[3, 3];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            result[i, j] = m[i, j] * s[j];
        return result;
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return r;
    }

    private static double[,] Invert(double[,] m)
    {
        var det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        return new[,]
        {
            { (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) / det, (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) / det, (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) / det },
            { (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) / det, (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) / det, (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) / det },
            { (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) / det, (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) / det, (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) / det },
        };
    }
}
