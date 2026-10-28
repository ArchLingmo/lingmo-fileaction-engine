using System.Runtime.InteropServices;
using System.Text.Json;
using FileAction.Core;
using FileAction.Linux;

namespace FileAction.Lingmo;

/// <summary>
/// Lingmo fork only (see LINGMO.md). The conversions without a window, for the Lingmo desktop
/// (lingmo-fileaction: the wheel, the Island progress, the app actions):
///
///   lingmo-fileaction-engine list [--json] FILE...
///   lingmo-fileaction-engine run [--json] ID FILE... [--param KEY=VALUE]... [--out DIR]
///   lingmo-fileaction-engine catalog [--json]
///   lingmo-fileaction-engine --version
///
/// With --json, "list" and "catalog" print one JSON document and "run" prints one JSON object per line:
///   {"event":"start","id":"image.to.webp","count":1}
///   {"event":"progress","value":0.42}
///   {"event":"done","outputs":["/home/me/a.webp"],"message":null,"note":null}
///   {"event":"error","code":"failed","message":"…","nothingToDo":false}
/// SIGTERM or SIGINT cancels a run (partial results are removed); exit codes: 0 done, 1 failed,
/// 2 bad arguments, 3 the needed program is not installed, 4 nothing to do, 130 cancelled.
/// </summary>
public static class Program
{
    private static readonly JsonWriterOptions Compact = new() { Indented = false };

    public static int Main(string[] args)
    {
        var settings = LinuxSettings.Load();
        Loc.Current = settings.Language ?? Loc.Detect();
        var json = args.Contains("--json");
        var rest = args.Where(a => a != "--json").ToArray();
        if (rest.Length == 0 || rest[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Help);
            return rest.Length == 0 ? 2 : 0;
        }
        try
        {
            return rest[0] switch
            {
                "list" => List(rest[1..], settings, json),
                "run" => Run(rest[1..], settings, json),
                "catalog" => Catalog(settings, json),
                "--version" => Print($"lingmo-fileaction-engine {typeof(Program).Assembly.GetName().Version?.ToString(3)}"),
                _ => Usage($"unknown command: {rest[0]}", json),
            };
        }
        catch (UsageException ex)
        {
            return Usage(ex.Message, json);
        }
    }

    private const string Help = """
        lingmo-fileaction-engine – FileAction conversions for the Lingmo desktop

          lingmo-fileaction-engine list [--json] FILE...
          lingmo-fileaction-engine run [--json] ID FILE... [--param KEY=VALUE]... [--out DIR]
          lingmo-fileaction-engine catalog [--json]
          lingmo-fileaction-engine --version
        """;

    private sealed class UsageException(string message) : Exception(message);

    private static int Print(string text)
    {
        Console.WriteLine(text);
        return 0;
    }

    private static int Usage(string message, bool json)
    {
        if (json)
            Emit(w => { w.WriteString("event", "error"); w.WriteString("code", "usage"); w.WriteString("message", message); });
        else
            Console.Error.WriteLine(message);
        return 2;
    }

    private static List<FileItem> Items(IEnumerable<string> paths)
    {
        var items = new List<FileItem>();
        foreach (var p in paths)
        {
            var path = p.StartsWith("file://", StringComparison.Ordinal) ? new Uri(p).LocalPath : p;
            var full = Path.GetFullPath(path);
            if (!File.Exists(full) && !Directory.Exists(full))
                throw new UsageException($"No such file: {p}");
            items.Add(new FileItem(full));
        }
        if (items.Count == 0)
            throw new UsageException("Give at least one file.");
        return items;
    }

    // ------------------------------------------------------------------ list / catalog

    private static int List(string[] args, LinuxSettings settings, bool json)
    {
        var items = Items(args);
        var tools = new ToolLocator();
        var all = LinuxOperations.Conversions(settings);
        if (!json)
        {
            foreach (var wheel in new[] { WheelKind.Convert, WheelKind.Tools })
            {
                var ops = all.Where(o => o.Wheel == wheel && o.AppliesTo(items)).ToList();
                if (ops.Count == 0)
                    continue;
                Console.WriteLine(wheel == WheelKind.Convert ? "Convert:" : "Tools:");
                foreach (var op in ops)
                {
                    var missing = op.IsAvailable(tools, items) ? "" : " [missing: " + string.Join(", ", Missing(op, tools)) + "]";
                    Console.WriteLine($"  {op.Id,-22} {op.Label}{(op.Subtitle is null ? "" : $" ({op.Subtitle})")}{missing}");
                }
            }
            return 0;
        }

        using var stream = Console.OpenStandardOutput();
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("engine", typeof(Program).Assembly.GetName().Version?.ToString(3));
        w.WriteStartArray("files");
        foreach (var item in items)
        {
            w.WriteStartObject();
            w.WriteString("path", item.Path);
            w.WriteString("name", item.Name);
            w.WriteString("format", item.Format);
            w.WriteBoolean("directory", item.IsDirectory);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartObject("wheels");
        foreach (var wheel in new[] { WheelKind.Convert, WheelKind.Tools })
        {
            w.WriteStartArray(wheel == WheelKind.Convert ? "convert" : "tools");
            foreach (var op in all.Where(o => o.Wheel == wheel && o.AppliesTo(items)))
                WriteOperation(w, op, tools, items, settings);
            w.WriteEndArray();
        }
        w.WriteEndObject();
        w.WriteEndObject();
        w.Flush();
        stream.WriteByte((byte)'\n');
        return 0;
    }

    private static int Catalog(LinuxSettings settings, bool json)
    {
        var tools = new ToolLocator();
        var all = LinuxOperations.Conversions(settings);
        if (!json)
        {
            foreach (var op in all)
                Console.WriteLine($"{op.Wheel,-8} {op.Id,-22} {op.Label}{(op.Subtitle is null ? "" : $" ({op.Subtitle})")}");
            return 0;
        }
        using var stream = Console.OpenStandardOutput();
        using var w = new Utf8JsonWriter(stream, Compact);
        w.WriteStartObject();
        w.WriteStartArray("operations");
        foreach (var op in all)
            WriteOperation(w, op, tools, null, settings);
        w.WriteEndArray();
        w.WriteStartObject("tools");
        foreach (var tool in Enum.GetValues<ExternalTool>())
            w.WriteBoolean(ToolLocator.DisplayName(tool), tools.Has(tool));
        w.WriteEndObject();
        w.WriteEndObject();
        w.Flush();
        stream.WriteByte((byte)'\n');
        return 0;
    }

    private static IEnumerable<string> Missing(Operation op, ToolLocator tools)
    {
        var missing = op.Requires.Where(t => !tools.Has(t)).Select(ToolLocator.DisplayName).ToList();
        if (missing.Count == 0 && op.Id == "tools.ocr")
            missing.Add("tesseract");
        return missing.Count == 0 ? ["?"] : missing;
    }

    private static void WriteOperation(Utf8JsonWriter w, Operation op, ToolLocator tools, IReadOnlyList<FileItem>? items, LinuxSettings settings)
    {
        w.WriteStartObject();
        w.WriteString("id", op.Id);
        w.WriteString("wheel", op.Wheel.ToString().ToLowerInvariant());
        w.WriteString("label", op.Label);
        w.WriteString("subtitle", op.Subtitle);
        // Convert slices are named "<kind>.to.<format>": the target format, for the app actions
        var to = op.Id.LastIndexOf(".to.", StringComparison.Ordinal);
        w.WriteString("target", op.Wheel == WheelKind.Convert && to > 0 ? op.Id[(to + 4)..] : null);
        w.WriteBoolean("preset", op.Id.StartsWith("preset.", StringComparison.Ordinal));
        var available = items is null ? op.Requires.All(tools.Has) : op.IsAvailable(tools, items);
        w.WriteBoolean("available", available);
        w.WriteStartArray("missing");
        if (!available)
        {
            foreach (var m in Missing(op, tools))
                w.WriteStringValue(m);
        }
        w.WriteEndArray();
        w.WriteStartArray("parameters");
        foreach (var p in op.Parameters)
        {
            w.WriteStartObject();
            w.WriteString("key", p.Key);
            w.WriteString("label", p.Label);
            w.WriteString("default", p.DefaultValue);
            w.WriteString("hint", p.Hint);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    // ------------------------------------------------------------------ run

    private static int Run(string[] args, LinuxSettings settings, bool json)
    {
        if (args.Length < 2)
            throw new UsageException("run ID FILE... [--param KEY=VALUE]... [--out DIR]");
        var parameters = new Dictionary<string, string>();
        string? output = null;
        var paths = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--param" && i + 1 < args.Length && args[i + 1].Split('=', 2) is [var k, var v])
            {
                parameters[k] = v;
                i++;
            }
            else if (args[i] == "--out" && i + 1 < args.Length)
            {
                output = Path.GetFullPath(args[++i]);
            }
            else
            {
                paths.Add(args[i]);
            }
        }
        var items = Items(paths);
        var tools = new ToolLocator();
        var op = LinuxOperations.Conversions(settings).FirstOrDefault(o => o.Id == args[0])
            ?? throw new UsageException($"Unknown ID: {args[0]} (see lingmo-fileaction-engine list)");
        if (!op.AppliesTo(items))
            throw new UsageException($"{op.Id} doesn't apply to these files.");
        if (!op.IsAvailable(tools, items))
            return Fail(json, "unavailable", $"{op.Id} needs {string.Join(", ", Missing(op, tools))}, which is not installed.", false, 3);

        using var cancel = new CancellationTokenSource();
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; cancel.Cancel(); });
        using var intr = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; cancel.Cancel(); });

        if (json)
            Emit(w => { w.WriteString("event", "start"); w.WriteString("id", op.Id); w.WriteNumber("count", items.Count); });
        var context = new OperationContext
        {
            Tools = tools,
            Parameters = parameters,
            OutputDirectory = output ?? (string.IsNullOrWhiteSpace(settings.OutputDirectory) ? null : settings.OutputDirectory),
            Settings = settings.Quality,
            CancellationToken = cancel.Token,
            Progress = json ? new JsonProgress() : new Progress<double>(),
        };
        try
        {
            var result = op.ExecuteAsync(items, context).GetAwaiter().GetResult();
            if (settings.WatchFolders.Count > 0)
                FileAction.Linux.Shell.RecentResults.Add(result.Outputs);
            if (json)
            {
                Emit(w =>
                {
                    w.WriteString("event", "done");
                    w.WriteStartArray("outputs");
                    foreach (var path in result.Outputs)
                        w.WriteStringValue(path);
                    w.WriteEndArray();
                    w.WriteString("message", result.Message);
                    w.WriteString("note", result.Note);
                });
            }
            else
            {
                foreach (var path in result.Outputs)
                    Console.WriteLine(path);
                if (result.Message is not null)
                    Console.Error.WriteLine(result.Message);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return Fail(json, "cancelled", "Cancelled.", false, 130);
        }
        catch (ConversionException ex) when (cancel.IsCancellationRequested)
        {
            _ = ex;
            return Fail(json, "cancelled", "Cancelled.", false, 130);
        }
        catch (ConversionException ex)
        {
            return Fail(json, ex.NothingToDo ? "nothing" : "failed", ex.Message, ex.NothingToDo, ex.NothingToDo ? 4 : 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(json, "failed", ex.Message, false, 1);
        }
    }

    private static int Fail(bool json, string code, string message, bool nothingToDo, int exit)
    {
        if (json)
        {
            Emit(w =>
            {
                w.WriteString("event", "error");
                w.WriteString("code", code);
                w.WriteString("message", message);
                w.WriteBoolean("nothingToDo", nothingToDo);
            });
        }
        else
        {
            Console.Error.WriteLine(message);
        }
        return exit;
    }

    private static readonly object EmitLock = new();

    /// <summary>One JSON object on one line of stdout, flushed at once (the app reads it live).</summary>
    private static void Emit(Action<Utf8JsonWriter> body)
    {
        lock (EmitLock)
        {
            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer, Compact))
            {
                w.WriteStartObject();
                body(w);
                w.WriteEndObject();
            }
            buffer.WriteByte((byte)'\n');
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
            stdout.Flush();
        }
    }

    /// <summary>Progress lines, at most one per whole percent (written as reported, not posted later).</summary>
    private sealed class JsonProgress : IProgress<double>
    {
        private int _last = -1;

        public void Report(double value)
        {
            var percent = (int)Math.Round(Math.Clamp(value, 0, 1) * 100);
            if (Interlocked.Exchange(ref _last, percent) == percent)
                return;
            Emit(w => { w.WriteString("event", "progress"); w.WriteNumber("value", Math.Round(Math.Clamp(value, 0, 1), 3)); });
        }
    }
}
