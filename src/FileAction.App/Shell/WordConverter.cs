using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FileAction.App.Shell;

/// <summary>
/// PDF → DOCX/ODT through Microsoft Word, when it is installed. Word's own PDF import keeps headings, tables,
/// pictures and lists far better than any text extraction. Word runs hidden on its own STA thread; if it
/// fails or hangs, the caller falls back to the built-in conversion.
/// </summary>
internal static class WordConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    // Word constants (WdSaveFormat, WdAlertLevel, MsoAutomationSecurity, WdSaveOptions).
    private const int FormatDocx = 16;
    private const int FormatOdt = 23;
    private const int AlertsNone = 0;
    private const int MacrosDisabled = 3;
    private const int DoNotSave = 0;

    private static readonly Lazy<bool> Installed = new(() => Type.GetTypeFromProgID("Word.Application") is not null);

    /// <summary>Checked once per run (the wheel asks every time it opens).</summary>
    public static bool IsInstalled => Installed.Value;

    public static async Task<bool> ConvertAsync(string pdf, string output, string format, CancellationToken ct)
    {
        if (Type.GetTypeFromProgID("Word.Application") is not { } type)
            return false;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = WordProcesses();
        var thread = new Thread(() => done.TrySetResult(Run(type, pdf, output, format))) { IsBackground = true, Name = "Word conversion" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Word is invisible, so any dialog it shows ("convert this PDF?", "this file caused an error before")
        // can never be answered: give up on the first one instead of waiting for the timeout.
        var deadline = DateTime.UtcNow + Timeout;
        while (!done.Task.IsCompleted && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.WhenAny(done.Task, Task.Delay(500, CancellationToken.None)).ConfigureAwait(false);
            if (!done.Task.IsCompleted && ShowsDialog(WordProcesses().Where(id => !before.Contains(id))))
                break;
        }
        if (done.Task.IsCompleted)
            return await done.Task.ConfigureAwait(false);
        // Hung (a dialog, a damaged PDF) or cancelled: stop the Word we started, never the user's own.
        foreach (var process in Process.GetProcessesByName("WINWORD").Where(p => !before.Contains(p.Id)))
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
        ct.ThrowIfCancellationRequested();
        return false;
    }

    private static bool ShowsDialog(IEnumerable<int> processIds)
    {
        var ids = processIds.ToHashSet();
        if (ids.Count == 0)
            return false;
        var found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (ids.Contains((int)pid) && IsWindowVisible(hwnd))
            {
                var name = new System.Text.StringBuilder(64);
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() is "#32770" or "NUIDialog" or "bosa_sdm_msword")
                    found = true;
            }
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int capacity);

    private static HashSet<int> WordProcesses() => Process.GetProcessesByName("WINWORD").Select(p => p.Id).ToHashSet();

    private static bool Run(Type type, string pdf, string output, string format)
    {
        object? word = null, documents = null, document = null;
        try
        {
            word = Activator.CreateInstance(type);
            Set(word!, "Visible", false);
            Set(word!, "DisplayAlerts", AlertsNone);
            Set(word!, "AutomationSecurity", MacrosDisabled);
            documents = Get(word!, "Documents");
            using var _ = SuppressPdfPrompt(Get(word!, "Version") as string);
            var missing = Type.Missing;
            // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument, PasswordTemplate,
            //      Revert, WritePasswordDocument, WritePasswordTemplate, Format, Encoding, Visible)
            document = Call(documents!, "Open", Path.GetFullPath(pdf), false, true, false, missing, missing, missing, missing, missing, missing, missing, false);
            Call(document!, "SaveAs2", Path.GetFullPath(output), format == "odt" ? FormatOdt : FormatDocx);
            return File.Exists(output) && new FileInfo(output).Length > 0;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidCastException or MissingMethodException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TryCall(document, "Close", DoNotSave);
            TryCall(word, "Quit", DoNotSave);
            foreach (var com in new[] { document, documents, word })
            {
                if (com is not null && Marshal.IsComObject(com))
                    Marshal.FinalReleaseComObject(com);
            }
        }
    }

    /// <summary>
    /// Opening a PDF makes Word ask "Word will now convert your PDF…", which nobody can answer in the background.
    /// Word's own option to skip that question is switched on for the conversion only and then put back.
    /// </summary>
    private static IDisposable SuppressPdfPrompt(string? version)
    {
        const string Name = "DisableConvertPdfWarning";
        var path = $@"Software\Microsoft\Office\{(string.IsNullOrEmpty(version) ? "16.0" : version)}\Word\Options";
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
            var previous = key.GetValue(Name);
            key.SetValue(Name, 1, Microsoft.Win32.RegistryValueKind.DWord);
            return new Restore(() =>
            {
                using var again = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path, writable: true);
                if (again is null)
                    return;
                if (previous is null)
                    again.DeleteValue(Name, throwOnMissingValue: false);
                else
                    again.SetValue(Name, previous, Microsoft.Win32.RegistryValueKind.DWord);
            });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return new Restore(() => { });
        }
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                undo();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
        }
    }

    // Late binding through IDispatch (works in the trimmed build, unlike C# dynamic).
    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static void TryCall(object? target, string name, params object[] args)
    {
        if (target is null)
            return;
        try
        {
            Call(target, name, args);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException)
        {
        }
    }
}
