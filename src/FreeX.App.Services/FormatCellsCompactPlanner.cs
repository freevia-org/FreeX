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
    CellColor? BorderColor = null,
    bool? DoubleUnderline = null,
    bool? ShrinkToFit = null,
    int? IndentLevel = null,
    int? TextRotation = null,
    string? FontName = null,
    bool? Locked = null,
    bool? Hidden = null,
    bool? Superscript = null,
    bool? Subscript = null);

public readonly record struct FormatCellsCompactBorderPresetMetadata(
    CellBorderPreset Preset,
    string DisplayName,
    bool RequiresPerCellPlanning);

public static class FormatCellsCompactPlanner
{
    private const double MinimumFontSize = 1.0;
    private const int MinimumIndentLevel = 0;
    private const int MaximumIndentLevel = 15;

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
            Superscript: request.Superscript,
            Subscript: request.Subscript,
            FontName: NormalizeFontName(request.FontName),
            FontSize: NormalizeFontSize(request.FontSize),
            FontColor: request.FontColor,
            FillColor: request.ClearFill ? null : request.FillColor,
            HAlign: request.HorizontalAlignment,
            VAlign: request.VerticalAlignment,
            WrapText: request.WrapText,
            ShrinkToFit: request.ShrinkToFit,
            NumberFormat: request.NumberFormat,
            DoubleUnderline: request.DoubleUnderline,
            IndentLevel: NormalizeIndentLevel(request.IndentLevel),
            TextRotation: NormalizeTextRotation(request.TextRotation),
            BorderTop: borderDiff?.BorderTop,
            BorderRight: borderDiff?.BorderRight,
            BorderBottom: borderDiff?.BorderBottom,
            BorderLeft: borderDiff?.BorderLeft,
            Locked: request.Locked,
            Hidden: request.Hidden,
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

    private static string? NormalizeFontName(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return null;

        return fontName.Trim();
    }

    private static int? NormalizeIndentLevel(int? indentLevel)
    {
        return indentLevel is null
            ? null
            : Math.Clamp(indentLevel.Value, MinimumIndentLevel, MaximumIndentLevel);
    }

    private static int? NormalizeTextRotation(int? textRotation)
    {
        if (textRotation is null)
            return null;

        var value = textRotation.Value;
        if (value == 255 || value is >= -90 and <= 90)
            return value;

        throw new ArgumentOutOfRangeException(nameof(FormatCellsCompactRequest.TextRotation), value, "Text rotation must be 255 or between -90 and 90 degrees.");
    }
}
