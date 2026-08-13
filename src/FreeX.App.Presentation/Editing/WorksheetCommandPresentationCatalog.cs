using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

public readonly record struct FillCommandPresentation(
    string CommandTitle,
    string CompletedAction);

public static class WorksheetCommandPresentationCatalog
{
    public static FillCommandPresentation DescribeFill(FillCellsDirection direction) =>
        direction switch
        {
            FillCellsDirection.Down => new("Fill Down", "Filled down"),
            FillCellsDirection.Right => new("Fill Right", "Filled right"),
            FillCellsDirection.Up => new("Fill Up", "Filled up"),
            FillCellsDirection.Left => new("Fill Left", "Filled left"),
            _ => new("Fill", "Filled")
        };

    public static string FormatFillFailure(FillCellsDirection direction) =>
        $"{DescribeFill(direction).CompletedAction} failed.";

    public static string FormatFillStatus(FillCellsDirection direction, string rangeReference) =>
        $"{DescribeFill(direction).CompletedAction} in {rangeReference}";

    public static string FormatHorizontalAlignmentStatus(string rangeReference, HorizontalAlignment alignment) =>
        $"Aligned {rangeReference} {HorizontalAlignmentLabel(alignment)}";

    public static string FormatVerticalAlignmentStatus(string rangeReference, VerticalAlignment alignment) =>
        $"Aligned {rangeReference} {VerticalAlignmentLabel(alignment)}";

    private static string HorizontalAlignmentLabel(HorizontalAlignment alignment) =>
        alignment switch
        {
            HorizontalAlignment.Left => "left",
            HorizontalAlignment.Center => "center",
            HorizontalAlignment.Right => "right",
            _ => "general"
        };

    private static string VerticalAlignmentLabel(VerticalAlignment alignment) =>
        alignment switch
        {
            VerticalAlignment.Top => "top",
            VerticalAlignment.Center => "middle",
            VerticalAlignment.Bottom => "bottom",
            _ => "middle"
        };
}
