using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record FormatCellsCompactRequest(
    string? NumberFormat = null,
    HorizontalAlignment? HorizontalAlignment = null,
    VerticalAlignment? VerticalAlignment = null,
    bool? WrapText = null,
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    bool? Strikethrough = null,
    double? FontSize = null,
    CellColor? FillColor = null,
    bool ClearFill = false,
    CellColor? FontColor = null,
    CellBorderPreset? BorderPreset = null,
    BorderStyle BorderStyle = BorderStyle.Thin,
    CellColor? BorderColor = null);

public readonly record struct FormatCellsCompactBorderPresetMetadata(
    CellBorderPreset Preset,
    string DisplayName,
    bool RequiresPerCellPlanning);

public static class FormatCellsCompactPlanner
{
    private const double MinimumFontSize = 1.0;

    private static readonly IReadOnlyList<FormatCellsCompactBorderPresetMetadata> BorderPresetMetadata =
        Enum.GetValues<CellBorderPreset>()
            .Select(preset => new FormatCellsCompactBorderPresetMetadata(
                preset,
                CellBorderPresetPlanner.GetDisplayName(preset),
                CellBorderPresetPlanner.RequiresPerCellPlanning(preset)))
            .ToArray();

    public static StyleDiff Plan(
        FormatCellsCompactRequest request,
        GridRange? borderRange = null,
        CellAddress? borderAddress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var borderDiff = request.BorderPreset is null
            ? null
            : PlanBorder(
                request.BorderPreset.Value,
                borderRange,
                borderAddress,
                request.BorderStyle,
                request.BorderColor);

        return new StyleDiff(
            Bold: request.Bold,
            Italic: request.Italic,
            Underline: request.Underline,
            Strikethrough: request.Strikethrough,
            FontSize: NormalizeFontSize(request.FontSize),
            FontColor: request.FontColor,
            FillColor: request.ClearFill ? null : request.FillColor,
            HAlign: request.HorizontalAlignment,
            VAlign: request.VerticalAlignment,
            WrapText: request.WrapText,
            NumberFormat: request.NumberFormat,
            BorderTop: borderDiff?.BorderTop,
            BorderRight: borderDiff?.BorderRight,
            BorderBottom: borderDiff?.BorderBottom,
            BorderLeft: borderDiff?.BorderLeft,
            ClearFill: request.ClearFill ? true : null);
    }

    public static bool TryPlan(
        FormatCellsCompactRequest request,
        out StyleDiff diff,
        out string errorMessage,
        GridRange? borderRange = null,
        CellAddress? borderAddress = null)
    {
        try
        {
            diff = Plan(request, borderRange, borderAddress);
            errorMessage = "";
            return true;
        }
        catch (ArgumentException ex)
        {
            diff = new StyleDiff();
            errorMessage = ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<FormatCellsCompactBorderPresetMetadata> GetBorderPresetMetadata() =>
        BorderPresetMetadata;

    private static StyleDiff PlanBorder(
        CellBorderPreset preset,
        GridRange? borderRange,
        CellAddress? borderAddress,
        BorderStyle borderStyle,
        CellColor? borderColor)
    {
        if (CellBorderPresetPlanner.RequiresPerCellPlanning(preset))
        {
            if (borderRange is null || borderAddress is null)
                throw new ArgumentException("Range-relative border presets require a selected range and cell address.");

            if (!borderRange.Value.Contains(borderAddress.Value))
                throw new ArgumentException("Border address must be inside the selected range.", nameof(borderAddress));

            return CellBorderPresetPlanner.Plan(preset, borderRange.Value, borderAddress.Value, borderStyle, borderColor);
        }

        var address = borderAddress ?? borderRange?.Start ?? CreateDefaultBorderAddress();
        var range = borderRange ?? new GridRange(address, address);
        return CellBorderPresetPlanner.Plan(preset, range, address, borderStyle, borderColor);
    }

    private static CellAddress CreateDefaultBorderAddress()
    {
        var sheet = SheetId.New();
        return new CellAddress(sheet, 1, 1);
    }

    private static double? NormalizeFontSize(double? fontSize)
    {
        if (fontSize is null)
            return null;

        var value = fontSize.Value;
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(FormatCellsCompactRequest.FontSize), value, "Font size must be a positive, finite value.");

        return Math.Max(MinimumFontSize, value);
    }
}
