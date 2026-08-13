using System.Reflection;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R65-render-cell-overflow-6-1
/// (src/FreeX.App.Host/MainWindow.HomeFormatting.cs's WrapTextBtn_Click and
/// MainWindow.CellsCommands.cs's ApplyFormatCellsDialogResult).
///
/// Before the fix: the WPF host's Wrap Text ribbon toggle (and the Format Cells dialog's Wrap Text
/// apply) routed through the generic ApplyStyleDiff, which flips the WrapText style bit but never
/// grows the row height -- so wrapped lines past the first were clipped at the default row height
/// (the Avalonia shell already had this auto-grow via WorkbookSession.SetSelectedRangeWrapText /
/// CreateWrapTextGrowthCommands, see WorkbookSessionWrapTextAutoGrowTests in
/// FreeX.App.Services.Tests).
///
/// After the fix, both call sites route through the new ApplyStyleDiffWithWrapGrowth /
/// CreateWrapTextGrowthCommands helpers (MainWindow.CellsCommands.cs), which fold the Excel-matching
/// "auto-grow unless manually resized" row-height command into the same undoable operation as the
/// WrapText style change.
/// </summary>
public sealed class R65_AppHostWrapTextAutoGrowTests
{
    [Fact]
    public void WrapTextBtn_Click_EnablingOnMultiLineContent_GrowsRowHeightFromDefault()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.DefaultColumnWidth = 8.43; // usable chars per line ~= 6 at this width.
                // 24 chars at ~6 usable chars/line wraps to ceil(24/6) = 4 visual lines, matching
                // the FreeX.App.Services.Tests WorkbookSessionWrapTextAutoGrowTests fixture.
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 24)));

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                SetWrapTextRibbonChecked(window, true);

                R49MainWindowTestHarness.Invoke(window, "WrapTextBtn_Click", null, null);

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight,
                    "enabling Wrap Text on multi-line-wrapping content must auto-grow the row, " +
                    "matching Excel and the Avalonia shell's WorkbookSession.SetSelectedRangeWrapText");
                var style = workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId);
                style.WrapText.Should().BeTrue();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void WrapTextBtn_Click_OnManuallyResizedTallerRow_DoesNotShrinkIt()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.RowHeights[1] = 120;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 24)));

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                SetWrapTextRibbonChecked(window, true);

                R49MainWindowTestHarness.Invoke(window, "WrapTextBtn_Click", null, null);

                sheet.RowHeights[1].Should().Be(120,
                    "a row the user already resized taller than what wrapping needs must be left " +
                    "alone, matching Excel never shrinking a manually-set row height");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: turning Wrap Text back OFF must still flip the style bit (as before the
    // fix) and must never touch row height -- the auto-grow only ever applies when enabling wrap.
    [Fact]
    public void WrapTextBtn_Click_DisablingWrapText_DoesNotTouchRowHeight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 24)));

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

                // Enable first (grows the row), then disable -- disabling must not shrink or
                // otherwise touch the row height it just grew.
                SetWrapTextRibbonChecked(window, true);
                R49MainWindowTestHarness.Invoke(window, "WrapTextBtn_Click", null, null);
                sheet.RowHeights.Should().ContainKey(1u);
                var grownHeight = sheet.RowHeights[1];

                SetWrapTextRibbonChecked(window, false);
                R49MainWindowTestHarness.Invoke(window, "WrapTextBtn_Click", null, null);

                sheet.RowHeights[1].Should().Be(grownHeight);
                var style = workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId);
                style.WrapText.Should().BeFalse();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Covers the Format Cells dialog's "simple" apply path (no border-range ops, no merge toggle),
    // which -- before the fix -- called the generic ApplyStyleDiff directly and skipped auto-grow.
    [Fact]
    public void ApplyFormatCellsDialogResult_SimplePathEnablingWrapText_GrowsRowHeight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.DefaultColumnWidth = 8.43;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 24)));

                var selectionRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                window.SheetGrid.SelectedRange = selectionRange;

                R49MainWindowTestHarness.Invoke(
                    window,
                    "ApplyFormatCellsDialogResult",
                    selectionRange,
                    new StyleDiff(WrapText: true),
                    FormatCellsDialogBorderSelection.None,
                    null,
                    MergeCellContentResolution.KeepFirstCell);

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Covers the Format Cells dialog's border/merge apply path (CreateRangeCommand local function),
    // which builds its own per-sheet ApplyStyleCommand outside of ApplyStyleDiff/ApplyStyleDiffWithWrapGrowth
    // and therefore needed its own explicit growth-command wiring.
    [Fact]
    public void ApplyFormatCellsDialogResult_BorderOpsPathEnablingWrapText_GrowsRowHeight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.DefaultColumnWidth = 8.43;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 24)));

                var selectionRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                window.SheetGrid.SelectedRange = selectionRange;

                R49MainWindowTestHarness.Invoke(
                    window,
                    "ApplyFormatCellsDialogResult",
                    selectionRange,
                    new StyleDiff(WrapText: true),
                    new FormatCellsDialogBorderSelection(Clear: true, Outline: null, Inside: null),
                    null,
                    MergeCellContentResolution.KeepFirstCell);

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetWrapTextRibbonChecked(MainWindow window, bool isChecked)
    {
        var ribbonStateField = typeof(MainWindow).GetField(
            "_ribbonState", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ribbonState = ribbonStateField.GetValue(window)!;
        var setChecked = ribbonState.GetType().GetMethod("SetChecked")!;
        setChecked.Invoke(ribbonState, [(Free.Shared.Ribbon.RibbonCommandId)"Wrap Text", isChecked]);
    }
}
