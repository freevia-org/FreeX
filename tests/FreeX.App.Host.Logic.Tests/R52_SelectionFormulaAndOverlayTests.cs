using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for round-52 findings in src/FreeX.App.Host/MainWindow.Selection.cs and
/// src/FreeX.App.Host/MainWindow.EditingDropdowns.cs:
///
/// R52-meta-2: the r51 "selection rectangle must never bisect a merged cell" fix
/// (ExpandRangeToFullyContainMerges) was applied to ExtendSelection but not mirrored to
/// AddOrMoveAdditionalSelection's extend-in-progress-additional-range path.
///
/// R52-render-formula-bar-ref-3-1: clicking/extending a column or row header while entering a
/// formula in point mode inserted a fully-qualified A1:A1048576-style reference instead of
/// Excel's bare "A:A"/"1:1" shorthand.
///
/// R52-render-formula-bar-ref-3-3: Ctrl+click while entering a formula reference in point mode
/// replaced the previously-inserted reference instead of appending a comma-separated disjoint
/// area, like Excel.
///
/// R52-render-scroll-viewport-nav-3-1: Ctrl+Home always jumped to A1 instead of the first
/// unfrozen cell when panes are frozen.
///
/// R52-commands-data-validation-apply-3-4: the in-cell DV dropdown/input-message overlay was
/// positioned from the single anchor cell's own row/column metrics instead of the full merged
/// block.
/// </summary>
public sealed class R52_SelectionFormulaAndOverlayTests
{
    // ── R52-meta-2 ──────────────────────────────────────────────────────────

    [Fact]
    public void AddOrMoveAdditionalSelection_ExtendPartiallyOverlappingMerge_SnapsToFullyContainIt()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 2)); // B2:B4
                sheet.AddMergedRegion(merge);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1)); // A1

                // Ctrl+click D1 starts a fresh additional selection area.
                R49MainWindowTestHarness.Invoke(
                    window, "AddOrMoveAdditionalSelection", new CellAddress(sheetId, 1, 4), false);

                // Ctrl-drag (mouse move while still held) down/left to B3 -- the raw rectangle
                // B1:D3 clips through rows 2-3 of the merge but excludes row 4.
                R49MainWindowTestHarness.Invoke(
                    window, "AddOrMoveAdditionalSelection", new CellAddress(sheetId, 3, 2), true);

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 4, 4)),
                    "the additional-selection rectangle must expand to fully contain the merge it partially " +
                    "overlaps (B1:D4), matching the r51 ExtendSelection fix for the primary drag path");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: extending an additional selection with no merge overlap is unaffected.
    [Fact]
    public void AddOrMoveAdditionalSelection_ExtendNoMergeOverlap_StaysExactlyAsComputed()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 1, 1));
                R49MainWindowTestHarness.Invoke(
                    window, "AddOrMoveAdditionalSelection", new CellAddress(sheetId, 1, 4), false);
                R49MainWindowTestHarness.Invoke(
                    window, "AddOrMoveAdditionalSelection", new CellAddress(sheetId, 3, 2), true);

                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 3, 4)));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // ── R52-render-formula-bar-ref-3-1 ──────────────────────────────────────

    [Fact]
    public void SelectColumn_DuringFormulaPointMode_InsertsColumnShorthand_NotFullyQualifiedRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                SetPrivateField(window, "_formulaEditCell", (CellAddress?)new CellAddress(sheet.Id, 1, 1));
                ConfigureFormulaRangeEditingSession(window);
                window.FormulaBar.Text = "=SUM(";
                window.FormulaBar.CaretIndex = window.FormulaBar.Text.Length;

                R49MainWindowTestHarness.Invoke(window, "SelectColumn", 1u); // Column A

                window.FormulaBar.Text.Should().Be("=SUM(A:A",
                    "Excel inserts the bare column shorthand (A:A), not the fully-qualified A1:A1048576 form");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void SelectRow_DuringFormulaPointMode_InsertsRowShorthand_NotFullyQualifiedRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                SetPrivateField(window, "_formulaEditCell", (CellAddress?)new CellAddress(sheet.Id, 1, 1));
                ConfigureFormulaRangeEditingSession(window);
                window.FormulaBar.Text = "=SUM(";
                window.FormulaBar.CaretIndex = window.FormulaBar.Text.Length;

                R49MainWindowTestHarness.Invoke(window, "SelectRow", 1u); // Row 1

                window.FormulaBar.Text.Should().Be("=SUM(1:1",
                    "Excel inserts the bare row shorthand (1:1), not the fully-qualified A1:XFD1 form");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ExtendHeaderSelection_ColumnBand_DuringFormulaPointMode_InsertsColumnRangeShorthand()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                SetPrivateField(window, "_formulaEditCell", (CellAddress?)new CellAddress(sheet.Id, 1, 1));
                ConfigureFormulaRangeEditingSession(window);
                window.FormulaBar.Text = "=SUM(";
                window.FormulaBar.CaretIndex = window.FormulaBar.Text.Length;

                R49MainWindowTestHarness.Invoke(
                    window, "ExtendHeaderSelection", FreeX.App.UI.GridHeaderContextMenuTarget.Column, 1u, 3u);

                window.FormulaBar.Text.Should().Be("=SUM(A:C",
                    "a multi-column header drag-extend while entering a formula must insert the bare " +
                    "column-band shorthand (A:C), not the fully-qualified A1:C1048576 form");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a genuine rectangular (non whole-row/column) range must NOT be
    // shortened -- only true whole-row/whole-column bands have an Excel shorthand.
    [Fact]
    public void FormatWholeRowOrColumnReferenceShorthand_PlainRectangularRange_ReturnsNull()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)); // A1:C3

        FormulaRangeEntryPlanner.FormatWholeRowOrColumnReferenceShorthand(range).Should().BeNull(
            "a genuine rectangular multi-cell range has no whole-row/column shorthand in Excel");
    }

    // Sibling no-regression: a whole-SHEET selection (both a full row band and a full column band
    // at once, e.g. Select All) has no bare Excel shorthand either and must be left alone.
    [Fact]
    public void FormatWholeRowOrColumnReferenceShorthand_WholeSheetSelection_ReturnsNull()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, CellAddress.MaxRow, CellAddress.MaxCol));

        FormulaRangeEntryPlanner.FormatWholeRowOrColumnReferenceShorthand(range).Should().BeNull(
            "Excel has no bare shorthand for a whole-sheet selection");
    }

    // ── R52-render-formula-bar-ref-3-3 ──────────────────────────────────────

    [Fact]
    public void TryAppendDisjointFormulaReference_CtrlClickAfterPriorReference_AppendsCommaSeparatedArea()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // Simulate the state right after a first (plain) click inserted "A1" at [5, 2) --
                // set the text FIRST, since FormulaBar's TextChanged handler clears any tracked
                // reference span whenever the caret is outside it, and the span fields below don't
                // exist yet at that point.
                window.FormulaBar.Text = "=SUM(A1";
                window.FormulaBar.CaretIndex = window.FormulaBar.Text.Length;
                SetPrivateField(window, "_formulaEditCell", (CellAddress?)new CellAddress(sheetId, 5, 5));
                ConfigureFormulaRangeEditingSession(window, referenceStart: 5, referenceLength: 2);

                var c3 = new CellAddress(sheetId, 3, 3);
                var appended = (bool)R49MainWindowTestHarness.Invoke(
                    window, "TryAppendDisjointFormulaReference", c3)!;

                appended.Should().BeTrue();
                window.FormulaBar.Text.Should().Be("=SUM(A1,C3",
                    "Ctrl+click must append a comma-separated disjoint area (matching Excel), not replace " +
                    "the previously-inserted reference");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: with no previously-tracked reference span (the very first click in
    // point mode), there is nothing to append after, so the append path must decline and leave the
    // formula text untouched -- the caller then falls through to the normal replacing path.
    [Fact]
    public void TryAppendDisjointFormulaReference_NoPriorReferenceSpan_ReturnsFalse_TextUnchanged()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                SetPrivateField(window, "_formulaEditCell", (CellAddress?)new CellAddress(sheet.Id, 5, 5));
                ConfigureFormulaRangeEditingSession(window);
                window.FormulaBar.Text = "=SUM(";

                var target = new CellAddress(sheet.Id, 3, 3);
                var appended = (bool)R49MainWindowTestHarness.Invoke(
                    window, "TryAppendDisjointFormulaReference", target)!;

                appended.Should().BeFalse();
                window.FormulaBar.Text.Should().Be("=SUM(");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // ── R52-render-scroll-viewport-nav-3-1 ──────────────────────────────────

    [Fact]
    public void GetHomeNavigationTarget_CtrlHeld_WithFrozenPanes_JumpsToFirstUnfrozenCell_NotA1()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.FrozenRows = 2;
                sheet.FrozenCols = 3;
                var current = new CellAddress(sheet.Id, 50, 26); // Z50, far from A1.

                var target = (CellAddress)R49MainWindowTestHarness.Invoke(
                    window, "GetHomeNavigationTarget", sheet, current, true)!;

                target.Should().Be(new CellAddress(sheet.Id, 3, 4),
                    "Ctrl+Home must jump to the top-left cell of the SCROLLABLE region (D3, the first " +
                    "unfrozen row/column) once panes are frozen, not always to A1");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: with no freeze active, Ctrl+Home still jumps to plain A1.
    [Fact]
    public void GetHomeNavigationTarget_CtrlHeld_NoFreeze_StillJumpsToA1()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var current = new CellAddress(sheet.Id, 50, 26);

                var target = (CellAddress)R49MainWindowTestHarness.Invoke(
                    window, "GetHomeNavigationTarget", sheet, current, true)!;

                target.Should().Be(new CellAddress(sheet.Id, 1, 1));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: plain Home (no Ctrl) still moves to column A of the CURRENT row,
    // unaffected by freeze -- only Ctrl+Home changes behavior for frozen panes.
    [Fact]
    public void GetHomeNavigationTarget_PlainHome_WithFrozenPanes_StillMovesToColumnAOfCurrentRow()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.FrozenRows = 2;
                sheet.FrozenCols = 3;
                var current = new CellAddress(sheet.Id, 50, 26);

                var target = (CellAddress)R49MainWindowTestHarness.Invoke(
                    window, "GetHomeNavigationTarget", sheet, current, false)!;

                target.Should().Be(new CellAddress(sheet.Id, 50, 1));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // ── R52-commands-data-validation-apply-3-4 ──────────────────────────────

    [Fact]
    public void GetOverlayAddressRange_CellInsideMerge_ReturnsFullMergeBounds()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var merge = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 3)); // A1:C1
                sheet.AddMergedRegion(merge);

                var result = ((CellAddress Start, CellAddress End))R49MainWindowTestHarness.Invoke(
                    window, "GetOverlayAddressRange", sheet, merge.Start)!;

                result.Should().Be((merge.Start, merge.End),
                    "a DV overlay anchored anywhere inside a merged cell must span the WHOLE merge (A1:C1), " +
                    "not just the anchor cell's own single-column metrics");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a plain (unmerged) cell still resolves to just itself.
    [Fact]
    public void GetOverlayAddressRange_PlainCell_ReturnsSingleCellRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 5, 5);

                var result = ((CellAddress Start, CellAddress End))R49MainWindowTestHarness.Invoke(
                    window, "GetOverlayAddressRange", sheet, addr)!;

                result.Should().Be((addr, addr));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object? value)
    {
        var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), fieldName);
        field.SetValue(window, value);
    }

    private static void ConfigureFormulaRangeEditingSession(
        MainWindow window,
        int? referenceStart = null,
        int? referenceLength = null)
    {
        var field = typeof(MainWindow).GetField(
            "_formulaRangeEditingSession",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_formulaRangeEditingSession");
        var session = (FormulaRangeEditingSession)field.GetValue(window)!;
        session.SetPointMode(true);
        session.TrackReferenceSpan(referenceStart, referenceLength);
    }
}
