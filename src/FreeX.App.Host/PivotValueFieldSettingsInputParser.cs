using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PivotValueNumberFormatPreset(string Label, int? NumberFormatId, string FormatCode);

public static class PivotValueFieldSettingsInputParser
{
    public const int DefaultCustomNumberFormatId = PivotValueFieldPlanner.DefaultCustomNumberFormatId;

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
        PivotValueFieldPlanner.NumberFormatPresets
            .Select(preset => new PivotValueNumberFormatPreset(
                UiText.Get(preset.ResourceKey),
                preset.NumberFormatId,
                preset.FormatCode))
            .ToArray();

    public static bool TryParseOptionalNumberFormatId(string input, out int? numberFormatId)
        => PivotValueFieldPlanner.TryParseOptionalNumberFormatId(input, out numberFormatId);

    public static string? ResolveOptionalNumberFormatCode(string input) =>
        PivotValueFieldPlanner.ResolveOptionalNumberFormatCode(input);

    public static int? ResolveNumberFormatIdForCode(int? numberFormatId, string? numberFormatCode) =>
        PivotValueFieldPlanner.ResolveNumberFormatIdForCode(numberFormatId, numberFormatCode);

    public static int? ResolvePresetNumberFormatId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return FindNumberFormatPreset(label.Trim())?.NumberFormatId;
    }

    public static string? ResolvePresetNumberFormatCode(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return FindNumberFormatPreset(label.Trim())?.FormatCode;
    }

    public static int? ResolveBuiltInNumberFormatIdForCode(string? formatCode) =>
        PivotValueFieldPlanner.ResolveBuiltInNumberFormatIdForCode(formatCode);

    public static bool TryResolveBuiltInNumberFormatIdForCode(string? formatCode, out int? numberFormatId)
        => PivotValueFieldPlanner.TryResolveBuiltInNumberFormatIdForCode(formatCode, out numberFormatId);

    private static PivotValueNumberFormatPreset? FindNumberFormatPreset(string label)
    {
        foreach (var preset in NumberFormatPresets)
        {
            if (string.Equals(preset.Label, label, StringComparison.OrdinalIgnoreCase))
                return preset;
        }

        return null;
    }

}
