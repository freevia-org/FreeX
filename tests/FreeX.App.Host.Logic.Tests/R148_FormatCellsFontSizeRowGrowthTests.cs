using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R148-remediation-wpf-formatcells-fontsize-rowgrowth-1: the WPF host's Format Cells dialog apply
/// path (MainWindow.CellsCommands.cs's ApplyFormatCellsDialogResult) grew affected rows only when
/// the diff turned Wrap Text on (R65-render-cell-overflow-6-1) -- a Font Size change made from the
/// dialog's Font tab applied the style but never grew the row, so the taller text was clipped.
/// The Avalonia shell has always auto-fitted here (WorkbookSession.ApplySelectedRangeCompactFormat
/// -> CreateSetFontSizeCommand -> CreateFontSizeRowGrowthCommands), as does the WPF host's own
/// ribbon Font Size box / +- buttons (ApplyFontSizeAndFitRows, MainWindow.HomeFormatting.cs).
///
/// These tests enter through ApplyFormatCellsDialogResult -- the method OpenFormatCellsDialog hands
/// the dialog's ResultDiff to -- and cover both of its branches (the simple no-border/no-merge path
/// and the border/merge CreateRangeCommand path), the grow-only/skip-hidden contract shared with
/// WorkbookSession.CreateFontSizeRowGrowthCommands, and the dialog-only case of Wrap Text and Font
/// Size arriving in the SAME apply.
/// </summary>
public sealed class R148_FormatCellsFontSizeRowGrowthTests
{
    private const double SeedFontSize = 36;

    private static double FittingRowHeight(double fontSize) =>
        Math.Min(AutoFitSizingService.MaximumRowHeight, FontSizePlanner.EstimateFittingRowHeight(fontSize));

    private static void ApplyFormatCells(
        MainWindow window,
        GridRange range,
        StyleDiff diff,
        FormatCellsDialogBorderSelection? borderSelection = null,
        bool? mergeCells = null) =>
        R49MainWindowTestHarness.Invoke(
            window,
            "ApplyFormatCellsDialogResult",
            range,
            diff,
            borderSelection ?? FormatCellsDialogBorderSelection.None,
            mergeCells,
            MergeCellContentResolution.KeepFirstCell);

    [Fact]
    public void ApplyFormatCellsDialogResult_SimplePathIncreasingFontSize_GrowsRowHeight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Tall"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                window.SheetGrid.SelectedRange = range;

                FittingRowHeight(SeedFontSize).Should().BeGreaterThan(sheet.DefaultRowHeight,
                    "the fixture is only meaningful if the new font size actually needs a taller row");

                ApplyFormatCells(window, range, new StyleDiff(FontSize: SeedFontSize));

                workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).FontSize.Should().Be(SeedFontSize);
                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().Be(FittingRowHeight(SeedFontSize),
                    "a Font Size change made from the Format Cells dialog must auto-fit the row, " +
                    "matching the ribbon's Font Size controls and the Avalonia shell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ApplyFormatCellsDialogResult_BorderOpsPathIncreasingFontSize_GrowsRowHeight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Tall"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                window.SheetGrid.SelectedRange = range;

                // The border/merge branch builds its own per-sheet command list instead of routing
                // through ApplyStyleDiffWithRowGrowth, so it needs its own growth wiring.
                ApplyFormatCells(
                    window,
                    range,
                    new StyleDiff(FontSize: SeedFontSize),
                    new FormatCellsDialogBorderSelection(Clear: true, Outline: null, Inside: null));

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().Be(FittingRowHeight(SeedFontSize));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ApplyFormatCellsDialogResult_IncreasingFontSize_KeepsTallerCustomRowAndHiddenRow()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;

                // Row 1: a banner row the user already sized taller than any font-driven fit.
                const double tallCustomHeight = 300.0;
                sheet.RowHeights[1] = tallCustomHeight;
                // Row 3: explicitly hidden by the user.
                sheet.HiddenRows.Add(3);

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));
                window.SheetGrid.SelectedRange = range;

                FittingRowHeight(SeedFontSize).Should().BeLessThan(tallCustomHeight,
                    "the test is only meaningful if a flat row-height write would have collapsed row 1");

                ApplyFormatCells(window, range, new StyleDiff(FontSize: SeedFontSize));

                sheet.RowHeights[1].Should().Be(tallCustomHeight,
                    "a font-size change must only ever GROW a row, never collapse a taller manual height");
                sheet.RowHeights.Should().ContainKey(2u);
                sheet.RowHeights[2].Should().Be(FittingRowHeight(SeedFontSize));
                sheet.HiddenRows.Should().Contain(3u,
                    "a font-size change must never un-hide a row caught inside the selection's row span");
                sheet.RowHeights.Should().NotContainKey(3u,
                    "the hidden row must not receive a new explicit height either");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Only the Format Cells dialog can turn Wrap Text on AND change the font size in one apply
    /// (the ribbon has a separate control per change), so only this path can produce two growth
    /// plans for the same row. Both are computed against the same pre-apply heights, so they must be
    /// merged by taking the taller height -- emitting both as commands would let the font-size one
    /// flatten the (taller) wrapped height.
    /// </summary>
    [Fact]
    public void ApplyFormatCellsDialogResult_WrapTextAndSmallerFontSizeTogether_KeepsTallerWrapHeight()
    {
        StaTestRunner.Run(() =>
        {
            // 14pt wants a row barely taller than the default; the wrapped content wants much more.
            const double modestFontSize = 14;
            var wrapOnlyHeight = MeasureRow1AfterApply(new StyleDiff(WrapText: true));

            wrapOnlyHeight.Should().BeGreaterThan(FittingRowHeight(modestFontSize),
                "the fixture is only meaningful if wrapping wants a taller row than the font size does");
            FittingRowHeight(modestFontSize).Should().BeGreaterThan(20,
                "...and only if the font size still wants growth of its own over the default height");

            var combinedHeight = MeasureRow1AfterApply(new StyleDiff(WrapText: true, FontSize: modestFontSize));

            combinedHeight.Should().Be(wrapOnlyHeight,
                "applying wrap and a font size together must keep the taller of the two growths");
        });
    }

    private static double MeasureRow1AfterApply(StyleDiff diff)
    {
        var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
        try
        {
            var sheet = workbook.GetSheetAt(0);
            var sheetId = sheet.Id;
            sheet.DefaultRowHeight = 20;
            sheet.DefaultColumnWidth = 8.43; // ~6 usable chars per line, as in R65_AppHostWrapTextAutoGrowTests.
            sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 60)));

            var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
            window.SheetGrid.SelectedRange = range;

            ApplyFormatCells(window, range, diff);

            sheet.RowHeights.Should().ContainKey(1u);
            return sheet.RowHeights[1];
        }
        finally
        {
            R49MainWindowTestHarness.Close(window);
        }
    }
}
