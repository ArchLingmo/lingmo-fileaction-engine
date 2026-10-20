using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.Linux;

public static class LinuxOperations
{
    /// <summary>Conversions and tools (no UI needed) followed by the user's presets, as used by the CLI and the wheel.</summary>
    public static IReadOnlyList<Operation> Conversions(LinuxSettings settings) =>
        [.. Catalog.Build(), new TesseractOcr(settings), .. settings.Presets.Select(PresetOperations.Build)];

    /// <summary>What a watched folder can run: no questions asked (rules run one file at a time).</summary>
    public static IReadOnlyList<Operation> ForWatching(LinuxSettings settings) =>
        [.. Conversions(settings).Where(o => o.Wheel != WheelKind.Actions && o.Parameters.Count == 0)];
}
