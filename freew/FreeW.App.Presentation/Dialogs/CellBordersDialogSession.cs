using System.Globalization;

namespace FreeW.App.Presentation.Dialogs;

public sealed record CellBordersDialogAcceptance(
    CellBordersDialogResult? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

/// <summary>
/// Renderer-neutral selection state of the Cell Borders dialog: the chosen preset and colour, whether
/// Apply is available yet, and the accept decision. Hosts only forward clicks and paint the controls.
/// </summary>
public sealed class CellBordersDialogSession
{
    /// <summary>The width text the dialog opens with.</summary>
    public const string DefaultWidthText = "0.5";

    /// <summary>The selected preset, or -1 until one is clicked.</summary>
    public int PresetIndex { get; private set; } = -1;

    /// <summary>The selected palette colour; the first swatch is selected initially.</summary>
    public int ColorIndex { get; private set; }

    /// <summary>Apply stays disabled until a preset has been chosen.</summary>
    public bool CanApply => PresetIndex >= 0;

    public bool IsColorSelected(int index) => index == ColorIndex;

    public void SelectPreset(int index) => PresetIndex = index;

    public void SelectColor(int index) => ColorIndex = index;

    public CellBordersDialogAcceptance PlanAcceptance(
        int styleIndex,
        string? widthText,
        CultureInfo culture) =>
        CellBordersDialogPlanner.TryBuildResult(
            new CellBordersDialogInput(PresetIndex, styleIndex, ColorIndex, widthText ?? string.Empty),
            culture,
            out var result,
            out var validationMessage)
            ? new CellBordersDialogAcceptance(result, ValidationMessage: null)
            : new CellBordersDialogAcceptance(null, validationMessage ?? CellBordersDialogPlanner.WidthValidationMessage);
}