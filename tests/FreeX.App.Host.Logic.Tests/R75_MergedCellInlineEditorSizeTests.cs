using System.Linq;
using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R75-render-merged-cells-4-2 (<c>MainWindow.Editing.cs</c>'s
/// <c>ShowInlineEditor</c>): the WPF in-cell editor for a MERGED cell was sized to the anchor
/// cell's single-cell box instead of the full merged rectangle, unlike
/// <c>GridView.Rendering.cs</c>'s <c>RenderCells</c>, which already widens/heightens for merges via
/// <c>GetMergeRegion</c>/<c>MergedRegions</c>.
/// </summary>
public sealed class R75_MergedCellInlineEditorSizeTests
{
    [Fact]
    public void ShowInlineEditor_OnHorizontallyMergedCell_SpansAllMergedColumnWidths()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var c1 = new CellAddress(sheet.Id, 1, 3);
                sheet.AddMergedRegion(new GridRange(a1, c1));

                var vp = window.SheetGrid.Viewport!;
                var expectedWidth =
                    ColWidth(vp, 1) + ColWidth(vp, 2) + ColWidth(vp, 3);

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", a1, (double?)null);

                var chromeRect = GetChromeBaseRect(window);
                chromeRect.Should().NotBeNull();
                chromeRect!.Value.Width.Should().BeApproximately(
                    expectedWidth,
                    0.01,
                    "editing a merge anchor must size the editor box to span the WHOLE merged " +
                    "rectangle's column widths (A1..C1), not just the anchor cell's own single-cell box " +
                    "(R75-render-merged-cells-4-2)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ShowInlineEditor_OnVerticallyMergedCell_SpansAllMergedRowHeights()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var b3 = new CellAddress(sheet.Id, 3, 2);
                var b4 = new CellAddress(sheet.Id, 4, 2);
                sheet.AddMergedRegion(new GridRange(b3, b4));

                var vp = window.SheetGrid.Viewport!;
                var expectedHeight = RowHeight(vp, 3) + RowHeight(vp, 4);

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", b3, (double?)null);

                var chromeRect = GetChromeBaseRect(window);
                chromeRect.Should().NotBeNull();
                chromeRect!.Value.Height.Should().BeApproximately(
                    expectedHeight,
                    0.01,
                    "editing a 2-row-tall merge anchor must span both merged rows' heights " +
                    "(R75-render-merged-cells-4-2)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ShowInlineEditor_OnNonMergedCell_StillSizesToSingleCellBox()
    {
        // Sibling no-regression: a plain, non-merged cell must keep its pre-existing single-cell
        // editor sizing untouched.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var e5 = new CellAddress(sheet.Id, 5, 5);

                var vp = window.SheetGrid.Viewport!;
                var expectedWidth = ColWidth(vp, 5);
                var expectedHeight = RowHeight(vp, 5);

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", e5, (double?)null);

                var chromeRect = GetChromeBaseRect(window);
                chromeRect.Should().NotBeNull();
                chromeRect!.Value.Width.Should().BeApproximately(expectedWidth, 0.01);
                chromeRect!.Value.Height.Should().BeApproximately(expectedHeight, 0.01);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static double ColWidth(FreeX.Core.Model.ViewportModel vp, uint col) =>
        vp.ColMetrics.First(m => m.Col == col).Width;

    private static double RowHeight(FreeX.Core.Model.ViewportModel vp, uint row) =>
        vp.RowMetrics.First(m => m.Row == row).Height;

    private static FormulaEditorRect? GetChromeBaseRect(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditorChromeBaseRect", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditorChromeBaseRect");
        return (FormulaEditorRect?)field.GetValue(window);
    }
}
