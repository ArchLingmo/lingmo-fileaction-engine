namespace FileAction.Core;

/// <summary>
/// An operation the user moved to another wheel (e.g. "Compress" onto Convert). Everything except
/// the wheel comes from <see cref="Inner"/>.
/// </summary>
public sealed class RelocatedOperation : Operation
{
    public RelocatedOperation(Operation inner, WheelKind wheel)
        : base(inner.Id, wheel, inner.Label, inner.Glyph, inner.Subtitle)
    {
        Inner = inner;
        Requires = inner.Requires;
        Parameters = inner.Parameters;
    }

    public Operation Inner { get; }

    /// <summary>The wheel the operation belongs to by default.</summary>
    public WheelKind HomeWheel => Inner.Wheel;

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => Inner.AppliesTo(items);

    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) => Inner.IsAvailable(tools, items);

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) => Inner.ExecuteAsync(items, ctx);

    /// <summary>Puts each operation on the wheel the user chose for it (unknown ids are ignored).</summary>
    public static IReadOnlyList<Operation> Apply(IEnumerable<Operation> operations, IReadOnlyDictionary<string, WheelKind> wheels) =>
        operations.Select(o => wheels.TryGetValue(o.Id, out var wheel) && wheel != o.Wheel ? new RelocatedOperation(o, wheel) : o).ToList();

    /// <summary>The operation without the move, e.g. to recognise an app action.</summary>
    public static Operation Unwrap(Operation op) => op is RelocatedOperation r ? r.Inner : op;
}

public static class WheelOrder
{
    /// <summary>
    /// Writes a new arrangement of one wheel into the global order: the slices in <paramref name="arranged"/>
    /// come first, in that order, followed by everything that was already ordered. Because the order is
    /// global, a slice keeps its place on every wheel it appears on.
    /// </summary>
    public static List<string> Merge(IReadOnlyList<string> arranged, IEnumerable<string> previous)
    {
        var set = arranged.ToHashSet();
        return [.. arranged, .. previous.Where(id => !set.Contains(id))];
    }

    /// <summary>Moves <paramref name="id"/> to <paramref name="index"/> (inserting it if it was not there).</summary>
    public static List<string> MoveTo(IReadOnlyList<string> ids, string id, int index)
    {
        var list = ids.Where(x => x != id).ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), id);
        return list;
    }
}
