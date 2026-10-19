using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;
using FileAction.App.Native;

namespace FileAction.App.Shell;

/// <summary>
/// Keeps one running copy. Later launches (e.g. from the Explorer context menu) hand their arguments to
/// the running copy over a named pipe and exit.
/// </summary>
/// <remarks>
/// Explorer starts one process per selected file, all at once, often while the first copy is still starting.
/// So the pipe is opened right away (requests wait in a queue until the app is ready), several requests can
/// connect at the same time, and senders keep trying for a while instead of giving up after one attempt.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    // Pipe names are machine-wide, so include the user; string.GetHashCode is randomized per process.
    private static readonly string DefaultId = "FileAction-" + new string(Environment.UserName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private const int Listeners = 4;
    private readonly Mutex _mutex;
    private readonly string _id;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<string[]> _pending = new();
    private readonly object _gate = new();
    private Dispatcher? _dispatcher;
    private Action<string[]>? _onArgs;

    private SingleInstance(Mutex mutex, string id)
    {
        _mutex = mutex;
        _id = id;
        for (var i = 0; i < Listeners; i++)
            _ = Task.Run(AcceptLoopAsync);
    }

    /// <param name="id">Another name than the app's (the self-test must not talk to a running FileAction).</param>
    public static SingleInstance? TryAcquire(string? id = null)
    {
        id ??= DefaultId;
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + id, out var created);
        if (created)
            return new SingleInstance(mutex, id);
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Sends arguments to the running copy, retrying while it starts up; returns false if it could not be
    /// reached within <paramref name="patience"/>.
    /// </summary>
    public static bool Forward(string[] args, TimeSpan? patience = null, string? id = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", id ?? DefaultId, PipeDirection.Out);
                client.Connect(2000);
                // Let the running copy bring its wheel to the front; we were started by the user so we may grant this.
                if (Win32.GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var pid))
                    Win32.AllowSetForegroundWindow((int)pid);
                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                writer.Write(string.Join("\n", args));
                writer.Flush();
                return true;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                    return false;
                Thread.Sleep(150);
            }
        }
    }

    /// <summary>Starts delivering requests (the queued ones first) to <paramref name="onArgs"/> on the UI thread.</summary>
    public void Listen(Dispatcher dispatcher, Action<string[]> onArgs)
    {
        lock (_gate)
        {
            _dispatcher = dispatcher;
            _onArgs = onArgs;
            while (_pending.TryDequeue(out var queued))
                _ = dispatcher.BeginInvoke(() => onArgs(queued));
        }
    }

    private void Deliver(string[] args)
    {
        lock (_gate)
        {
            if (_dispatcher is { } dispatcher && _onArgs is { } onArgs)
                _ = dispatcher.BeginInvoke(() => onArgs(args));
            else
                _pending.Enqueue(args);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_id, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(_cts.Token);
                var args = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 0)
                    Deliver(args);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }
}
