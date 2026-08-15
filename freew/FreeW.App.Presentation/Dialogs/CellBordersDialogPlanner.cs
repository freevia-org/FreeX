using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record CellBordersPreset(
    string Label,
    CellBorderEdges Edges,
    bool ClearEdges = false);

public sealed record CellBordersDialogInput(
    int PresetIndex,
    int StyleIndex,
    int ColorIndex,
    string WidthText);

public sealed record CellBordersDialogResult(
    CellBorderEdges Edges,
    BorderLineStyle Style,
    string ColorHex,
    double WidthPt,
    bool ClearEdges);

public sealed record CellBordersDialogText(
    string Title,
    string PresetLabel,
    string StyleLabel,
    string ColorLabel,
    string WidthLabel,
    string ApplyLabel,
    string CancelLabel);

/// <summary>
/// Renderer-neutral policy for Table Design &gt; Borders. Both hosts project the same preset ordering,
/// line styles, palette, validation, and clear-border semantics; native code only renders controls.
/// </summary>
public static class CellBordersDialogPlanner
{
    public const string Title = "Cell Borders";
    public const string PresetLabel = "Preset:";
    public const string StyleLabel = "Style:";
    public const string ColorLabel = "Colour:";
    public const string WidthLabel = "Width (pt):";
    public const string ApplyLabel = "Apply";
    public const string CancelLabel = "Cancel";
    public const string WidthValidationMessage = "Enter a border width between 0 and 12 points.";
    public const string AutomationId = "CellBordersDialog";
    public const string PresetAutomationId = "CellBordersPreset";
    public const string StyleAutomationId = "CellBordersStyle";
    public const string ColorAutomationId = "CellBordersColor";
    public const string WidthAutomationId = "CellBordersWidth";
    public const string ValidationAutomationId = "CellBordersValidation";

    public static IReadOnlyList<CellBordersPreset> Presets { get; } =
    [
        new("All", CellBorderEdges.All),
        new("Outside", CellBorderEdges.Outside),
        new("Inside", CellBorderEdges.Inside),
        new("Top", CellBorderEdges.Top),
        new("Bottom", CellBorderEdges.Bottom),
        new("Left", CellBorderEdges.Left),
        new("Right", CellBorderEdges.Right),
        new("None", CellBorderEdges.All, ClearEdges: true),
    ];

    public static IReadOnlyList<string> LineStyleNames => BordersAndShadingDialogPlanner.LineStyleNames;
    public static IReadOnlyList<BorderLineStyle> LineStyleValues => BordersAndShadingDialogPlanner.LineStyleValues;

    public static IReadOnlyList<string> Palette { get; } =
    [
        "#000000", "#FF0000", "#0000FF", "#008000", "#800000",
        "#808080", "#C0C0C0", "#FF6600", "#9900CC", "#FFFFFF",
    ];

    public static CellBordersDialogText ResolveText(Func<string, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return new CellBordersDialogText(
            resolve("CellBorders_Title"),
            resolve("Border_Preset_Label"),
            resolve("Border_Style_Label"),
            resolve("Design_PageColor_Color_Label"),
            resolve("ChartSize_Width_Label"),
            resolve("Common_Apply"),
            resolve("Common_CancelText"));
    }

    public static bool TryBuildResult(
        CellBordersDialogInput input,
        CultureInfo culture,
        out CellBordersDialogResult result,
        out string? validationMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        if (!double.TryParse(input.WidthText, NumberStyles.Float, culture, out var widthPt)
            || !double.IsFinite(widthPt)
            || widthPt <= 0
            || widthPt > 12)
        {
            result = default!;
            validationMessage = WidthValidationMessage;
            return false;
        }

        var preset = Presets[Math.Clamp(input.PresetIndex, 0, Presets.Count - 1)];
        var style = LineStyleValues[Math.Clamp(input.StyleIndex, 0, LineStyleValues.Count - 1)];
        var color = Palette[Math.Clamp(input.ColorIndex, 0, Palette.Count - 1)];
        result = new CellBordersDialogResult(
            preset.Edges,
            style,
            color,
            widthPt,
            preset.ClearEdges);
        validationMessage = null;
        return true;
    }
}
