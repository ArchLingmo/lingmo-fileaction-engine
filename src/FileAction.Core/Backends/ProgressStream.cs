namespace FileAction.Core.Backends;

/// <summary>
/// A read-only wrapper that reports every byte read and stops as soon as the job is cancelled, so long
/// archive work shows real progress and Cancel works in the middle of a 4 GB file.
/// </summary>
internal sealed class ProgressStream(Stream inner, Action<long> onRead, CancellationToken ct, bool leaveOpen = false) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ct.ThrowIfCancellationRequested();
        var read = inner.Read(buffer, offset, count);
        if (read > 0)
            onRead(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        ct.ThrowIfCancellationRequested();
        var read = inner.Read(buffer);
        if (read > 0)
            onRead(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ct.ThrowIfCancellationRequested();
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
            onRead(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
            inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Turns byte counts into a 0–1 progress value within a range (e.g. extracting is the first half of a repack).</summary>
internal sealed class ByteProgress(long total, IProgress<double>? progress, double from = 0, double to = 1)
{
    private long _done;
    private double _last = -1;

    public void Add(long bytes)
    {
        if (progress is null || total <= 0)
            return;
        var done = Interlocked.Add(ref _done, bytes);
        var value = from + (to - from) * Math.Clamp(done / (double)total, 0, 1);
        // Reporting every 80 KB read would flood the UI; whole tenths of a percent are plenty.
        if (value - _last < 0.001 && value < to)
            return;
        _last = value;
        progress.Report(value);
    }

    /// <summary>For readers that seek around (7z): progress from the position in the file, not a running sum.</summary>
    public void Set(long position)
    {
        if (progress is null || total <= 0)
            return;
        var value = from + (to - from) * Math.Clamp(position / (double)total, 0, 1);
        if (Math.Abs(value - _last) < 0.001)
            return;
        _last = value;
        progress.Report(value);
    }
}
