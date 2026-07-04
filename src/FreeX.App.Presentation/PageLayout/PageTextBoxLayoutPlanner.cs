using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Resolves worksheet text boxes into portable page-space blocks. Renderers still own platform text
/// drawing, clipping, and selectable-text overlays; this planner owns the shared print placement,
/// page inclusion, minimum size, text inset, and effective colors.
/// </summary>
public static class PageTextBoxLayoutPlanner
{
    /// <summary>Minimum printed text-box width in device-independent units, matching the WPF print renderer.</summary>
    public const double MinimumWidth = TextBoxFrameLayoutPlanner.MinimumWidth;

    /// <summary>Minimum printed text-box height in device-independent units, matching the WPF print renderer.</summary>
    public const double MinimumHeight = TextBoxFrameLayoutPlanner.MinimumHeight;

    /// <summary>Inset between a printed text-box border and its text content.</summary>
    public const double TextInset = TextBoxFrameLayoutPlanner.TextInset;

    /// <summary>Alpha used for text-box fills by the source WPF print renderer.</summary>
    public const byte FillAlpha = 242;

    public static IReadOnlyList<PageTextBoxBlock> Build(
        IReadOnlyList<TextBoxModel> textBoxes,
        WorkbookTheme workbookTheme,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(textBoxes);
        ArgumentNullException.ThrowIfNull(workbookTheme);
        ArgumentNullException.ThrowIfNull(pageRows);
        ArgumentNullException.ThrowIfNull(pageColumns);
        ArgumentNullException.ThrowIfNull(measurement);

        if (textBoxes.Count == 0 || pageRows.Count == 0 || pageColumns.Count == 0)
            return [];

        var rowIndexes = BuildPositionLookup(pageRows);
        var columnIndexes = BuildPositionLookup(pageColumns);
        var blocks = new List<PageTextBoxBlock>();
        foreach (var textBox in textBoxes)
        {
            if (!textBox.IsVisible ||
                !rowIndexes.TryGetValue(textBox.Anchor.Row, out var rowIndex) ||
                !columnIndexes.TryGetValue(textBox.Anchor.Col, out var columnIndex))
            {
                continue;
            }

            var layout = TextBoxFrameLayoutPlanner.CreateNormalized(new LayoutRect(
                gridLeft + measurement.ColumnOffset(columnIndex),
                gridTop + measurement.RowOffset(rowIndex),
                textBox.Width,
                textBox.Height));
            var fill = textBox.ResolveFillColor(workbookTheme, CellColor.White);
            var outline = textBox.GetEffectiveOutlineColor(workbookTheme, new CellColor(89, 89, 89));

            blocks.Add(new PageTextBoxBlock(
                textBox.Id,
                layout.Bounds,
                layout.TextBounds,
                textBox.Text,
                fill is { } fillColor ? PresentationRgb.FromCellColor(fillColor) : null,
                FillAlpha,
                PresentationRgb.FromCellColor(outline),
                OutlineThickness: 1,
                new PageTextFont(
                    PageContentRenderModelBuilder.PrintFontFamily,
                    PageContentRenderModelBuilder.PrintFontSize,
                    Bold: false,
                    Italic: false,
                    new PresentationRgb(0, 0, 0))));
        }

        return blocks;
    }

    private static IReadOnlyDictionary<uint, int> BuildPositionLookup(IReadOnlyList<uint> indexes)
    {
        var lookup = new Dictionary<uint, int>(indexes.Count);
        for (var i = 0; i < indexes.Count; i++)
            lookup[indexes[i]] = i;

        return lookup;
    }
}
