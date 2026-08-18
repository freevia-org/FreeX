using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests and benchmarks for the whole-column / whole-row style performance fix.
/// Option B: used-range clamp — new style-only entries are only created for empty cells within
/// the used-range bounding box; content cells anywhere in the selection always get styled.
/// </summary>
public sealed class ApplyStyleCommandPerformanceTests
{
    // ── StyleOnlyCreateZone unit tests ───────────────────────────────────────

    [Fact]
    public void StyleOnlyCreateZone_EmptySheet_CapsAtRow1000()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var wholeCol = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, wholeCol);

        // Empty sheet: zone should be capped (not MaxRow)
        zone.Should().NotBeNull("a zone should be returned for an empty sheet");
        zone!.Value.End.Row.Should().BeLessThanOrEqualTo(1_000,
            "whole-column on empty sheet must be capped to avoid materialising 1M style-only entries");
    }

    [Fact]
    public void StyleOnlyCreateZone_SheetWith100Rows_ClampsToUsedRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 100; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var wholeCol = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, wholeCol);

        zone.Should().NotBeNull();
        zone!.Value.End.Row.Should().Be(100,
            "the style-only create zone must be clamped to the used range's max row");
    }

    [Fact]
    public void StyleOnlyCreateZone_PartialColumnWithContent_IntersectsCorrectly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Content in rows 50-150
        for (uint r = 50; r <= 150; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 3), new NumberValue(r));

        // Select whole column (rows 1..MaxRow)
        var wholeCol = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, wholeCol);

        zone.Should().NotBeNull();
        zone!.Value.Start.Row.Should().Be(50, "zone must start at the first used row");
        zone!.Value.End.Row.Should().Be(150, "zone must end at the last used row");
    }

    [Fact]
    public void StyleOnlyCreateZone_WholeColumnWhenDataInDifferentColumn_ReturnsCrossUsedRows()
    {
        // Regression: formatting column A (col 1) when data lives only in col 5 previously
        // intersected BOTH dimensions and returned null, causing Bold on column A to silently
        // style nothing.  The fix: only clamp the unbounded (row) dimension; keep the selected
        // (bounded) column as-is.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Content only in column 5, row 1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(42));

        // Select whole column 1 — data lives in a different column
        var col1 = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, col1);

        // Rows clamp to used-range rows (row 1..1); columns stay as selected (col 1..1).
        zone.Should().NotBeNull(
            "whole-column selection must return a zone even when no data exists in that column");
        zone!.Value.Start.Col.Should().Be(1, "the bounded column dimension must not be intersected away");
        zone!.Value.End.Col.Should().Be(1, "the bounded column dimension must not be intersected away");
        zone!.Value.Start.Row.Should().Be(1, "rows clamp to used-range start row");
        zone!.Value.End.Row.Should().Be(1, "rows clamp to used-range end row");
    }

    [Fact]
    public void StyleOnlyCreateZone_WholeRowWhenDataInDifferentRow_ReturnsCrossUsedCols()
    {
        // Symmetric to the whole-column case: formatting row 5 when data lives only in row 1.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(99));

        var wholeRow5 = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 5, CellAddress.MaxCol));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, wholeRow5);

        zone.Should().NotBeNull(
            "whole-row selection must return a zone even when no data exists in that row");
        zone!.Value.Start.Row.Should().Be(5, "the bounded row dimension must not be intersected away");
        zone!.Value.End.Row.Should().Be(5, "the bounded row dimension must not be intersected away");
        zone!.Value.Start.Col.Should().Be(3, "cols clamp to used-range start col");
        zone!.Value.End.Col.Should().Be(3, "cols clamp to used-range end col");
    }

    // ── F1: unbounded selection starting mid-sheet must not leak above/left of Start ──────────

    [Fact]
    public void StyleOnlyCreateZone_UnboundedRowsStartingMidSheet_DoesNotLeakAboveSelectionStart()
    {
        // Regression for range-arithmetic F1: a range like B5:B1048576 (a Ctrl+Shift+Down
        // selection, NOT a column-header click -- Start.Row is 5, not 1) reaches MaxRow so it is
        // still treated as "unbounded" for perf-clamp purposes, but the zone must never start
        // above the selection's OWN Start.Row even when the sheet has used-range content above it
        // (e.g. a header row at row 1).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Data in column A, rows 1-20 (establishes a used range starting at row 1).
        for (uint r = 1; r <= 20; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        // Selection: B5:B1048576 -- starts at row 5, mid-sheet, reaches MaxRow.
        var midSheetToBottom = new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, midSheetToBottom);

        zone.Should().NotBeNull();
        zone!.Value.Start.Row.Should().Be(5,
            "the zone must start at the selection's own Start.Row, not the sheet's used-range start row");
    }

    [Fact]
    public void StyleOnlyCreateZone_UnboundedColsStartingMidSheet_DoesNotLeakLeftOfSelectionStart()
    {
        // Symmetric sibling: a row-suffix selection like E5:XFD5 (bounded start column reaching
        // MaxCol) must not leak formatting leftward into unselected columns.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Data in row 1, columns A-T (establishes a used range starting at column 1).
        for (uint c = 1; c <= 20; c++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, c), new NumberValue(c));

        // Selection: E5:XFD5 -- starts at column 5, mid-sheet, reaches MaxCol.
        var midSheetToRight = new GridRange(
            new CellAddress(sheet.Id, 5, 5),
            new CellAddress(sheet.Id, 5, CellAddress.MaxCol));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, midSheetToRight);

        zone.Should().NotBeNull();
        zone!.Value.Start.Col.Should().Be(5,
            "the zone must start at the selection's own Start.Col, not the sheet's used-range start col");
    }

    [Fact]
    public void StyleOnlyCreateZone_TrueWholeColumn_UnaffectedByMidSheetStartFix()
    {
        // No-regression sibling: a GENUINE whole-column selection (Start.Row == 1, e.g. a real
        // column-header click) must still clamp its start row to the used range's start row exactly
        // as before -- Math.Max(usedRange.Start.Row, range.Start.Row) with range.Start.Row == 1
        // reduces to usedRange.Start.Row when the used range starts below row 1... which it never
        // does, so this must equal usedRange.Start.Row.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 50; r <= 150; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 3), new NumberValue(r));

        var wholeCol = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));

        var zone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, wholeCol);

        zone.Should().NotBeNull();
        zone!.Value.Start.Row.Should().Be(50, "a true whole-column selection still clamps to the used-range start row");
        zone!.Value.End.Row.Should().Be(150, "a true whole-column selection still clamps to the used-range end row");
    }

    [Fact]
    public void ApplyBold_UnboundedRowsStartingMidSheet_DoesNotStyleRowsAboveSelection()
    {
        // End-to-end reproduction of the reported user gesture: header row 1, then Bold applied to
        // B5:B1048576 (Name-Box entry, or Ctrl+Shift+Down from B5 with no data below). Rows B1:B4
        // must NOT become bold-styled -- they were never part of the selection.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 20; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r)); // header/data in col A

        var ctx = new TestCommandContext(wb);
        var midSheetToBottom = new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));

        var cmd = new ApplyStyleCommand(sheet.Id, midSheetToBottom, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetStyleOnly(1, 2).Should().BeNull("row 1 col B was never part of the selection and must not be styled");
        sheet.GetStyleOnly(2, 2).Should().BeNull("row 2 col B was never part of the selection and must not be styled");
        sheet.GetStyleOnly(3, 2).Should().BeNull("row 3 col B was never part of the selection and must not be styled");
        sheet.GetStyleOnly(4, 2).Should().BeNull("row 4 col B was never part of the selection and must not be styled");

        var styleAtB5 = sheet.GetStyleOnly(5, 2);
        styleAtB5.Should().NotBeNull("row 5 col B WAS part of the selection and must be styled");
        wb.GetStyle(styleAtB5!.Value).Bold.Should().BeTrue();
    }

    [Fact]
    public void DetermineStyleOnlySource_UnboundedRowsStartingMidSheet_IsNotColumnSourced()
    {
        // Regression for the classification half of F1: DetermineStyleOnlySource must agree with
        // SelectionRangeService.IsWholeColumnSelection, which requires Start.Row == 1. A range that
        // merely reaches MaxRow while starting mid-sheet is not a genuine column-header selection
        // and must not carry StyleOnlySource.Column provenance.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var midSheetToBottom = new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));

        var source = ApplyStyleCommand.DetermineStyleOnlySource(midSheetToBottom);

        source.Should().BeNull("a selection that doesn't start at row 1 is not a genuine column-header selection");
    }

    [Fact]
    public void DetermineStyleOnlySource_TrueWholeColumn_IsStillColumnSourced()
    {
        // No-regression sibling: a genuine whole-column selection (Start.Row == 1) must still be
        // classified as StyleOnlySource.Column, preserving the row-beats-column precedence feature.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var wholeCol = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));

        var source = ApplyStyleCommand.DetermineStyleOnlySource(wholeCol);

        source.Should().Be(StyleOnlySource.Column);
    }

    // ── Empty-column / empty-row bold regression tests ───────────────────────

    [Fact]
    public void WholeColumnBold_EmptyColumnWithDataElsewhere_CreatesStyleOnlyEntriesInUsedRows()
    {
        // Regression: bold on column A (empty) when data lives in B:D should create style-only
        // entries for the used-range rows in column A, not silently no-op.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Data in columns B..D, rows 1..5 — column A is empty
        for (uint r = 1; r <= 5; r++)
            for (uint c = 2; c <= 4; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * 10 + c));

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        var result = cmd.Apply(ctx);

        result.Success.Should().BeTrue("bold on an empty column must succeed");

        // Style-only entries must exist in column A for the used-range rows (rows 1..5)
        sheet.StyleOnlyCellCount.Should().BeGreaterThan(0,
            "bold on an empty column must create style-only entries so a cell typed later appears bold");

        // Verify at least one of the expected rows has a style-only entry
        var styleAtA1 = sheet.GetStyleOnly(1, 1);
        styleAtA1.Should().NotBeNull("row 1 col A must have a style-only entry after whole-column bold");
        wb.GetStyle(styleAtA1!.Value).Bold.Should().BeTrue("the style-only entry must be bold");

        // Cells beyond the used row range must NOT get style-only entries
        sheet.GetStyleOnly(100, 1).Should().BeNull(
            "rows beyond the used range must not get style-only entries");

        // Undo must remove the style-only entries
        cmd.Revert(ctx);
        sheet.GetStyleOnly(1, 1).Should().BeNull("undo must remove style-only entries from column A");
        sheet.StyleOnlyCellCount.Should().Be(0, "undo must leave no style-only entries");
    }

    [Fact]
    public void WholeColumnBold_EmptyColumnNoData_CapsAtRow1000AndTypedCellIsStyled()
    {
        // On a fully empty sheet, whole-column bold caps at 1,000 rows.
        // A cell typed later at row 500 (within the cap) must be covered by the style-only entry.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Style-only entries must exist (capped at 1,000 rows)
        sheet.StyleOnlyCellCount.Should().BeGreaterThan(0,
            "empty sheet whole-column bold must create style-only entries up to the row cap");
        sheet.StyleOnlyCellCount.Should().BeLessThanOrEqualTo(1_000,
            "empty sheet whole-column bold must not exceed the 1,000-row cap");

        // A cell typed later in the covered range must get the style from the style-only entry
        var styleAtRow500 = sheet.GetStyleOnly(500, 1);
        styleAtRow500.Should().NotBeNull("row 500 must be within the style-only zone");
        wb.GetStyle(styleAtRow500!.Value).Bold.Should().BeTrue();
    }

    // ── ApplyStyleCommand whole-column behaviour tests ───────────────────────

    [Fact]
    public void WholeColumnBold_With100UsedCells_StyleOnlyCountBoundedByUsedRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        const uint usedRows = 100;

        for (uint r = 1; r <= usedRows; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // The style-only cell count must not explode to MaxRow.
        // Content cells are styled via the cell path, style-only entries only for empty cells
        // within the used range (there are none here since all 100 rows have content).
        sheet.StyleOnlyCellCount.Should().Be(0,
            "all 100 cells have content — no style-only entries should be created");

        // All 100 content cells must have been styled.
        for (uint r = 1; r <= usedRows; r++)
        {
            var cell = sheet.GetCell(r, 1);
            cell.Should().NotBeNull();
            wb.GetStyle(cell!.StyleId).Bold.Should().BeTrue($"row {r} must be bold");
        }
    }

    [Fact]
    public void WholeColumnBold_WithMixedContentAndEmpty_StyleOnlyBoundedAndContentFullyStyled()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Content cells at rows 1-100
        for (uint r = 1; r <= 100; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Style-only count must be bounded — rows 1-100 all have content so 0 style-only entries
        sheet.StyleOnlyCellCount.Should().Be(0);

        // Rows beyond the used range must NOT have style-only entries
        sheet.GetStyleOnly(200, 1).Should().BeNull(
            "cells far beyond the used range must not get style-only entries");
        sheet.GetStyleOnly(CellAddress.MaxRow, 1).Should().BeNull(
            "the last row of a whole-column selection must not get a style-only entry");
    }

    [Fact]
    public void WholeColumnBold_UndoExact_NoStyleOnlyTrace()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 50; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));

        var ctx = new TestCommandContext(wb);
        var wholeColB = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));

        // Snapshot original style IDs
        var originalStyleIds = new Dictionary<uint, StyleId>();
        for (uint r = 1; r <= 50; r++)
            originalStyleIds[r] = sheet.GetCell(r, 2)!.StyleId;

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColB, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Verify bold was applied
        for (uint r = 1; r <= 50; r++)
            wb.GetStyle(sheet.GetCell(r, 2)!.StyleId).Bold.Should().BeTrue();

        cmd.Revert(ctx);

        // After undo: original style IDs must be restored
        for (uint r = 1; r <= 50; r++)
            sheet.GetCell(r, 2)!.StyleId.Should().Be(originalStyleIds[r], $"row {r} style must be restored after undo");

        // No style-only entries remain
        sheet.StyleOnlyCellCount.Should().Be(0,
            "undo must remove all style-only entries created by the command");
        sheet.GetStyleOnly(1, 2).Should().BeNull("undo must remove style-only from styled rows");
    }

    [Fact]
    public void WholeColumnBold_ContentCellBeyondUsedRangeBbox_GetsStyled()
    {
        // This tests the rule: content cells at ANY position in the selection get styled,
        // even if beyond the bounding box of other content (Option B gating rule).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Most content in rows 1-10 col 1
        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        // One content cell far away — row 50,000 col 1
        const uint farRow = 50_000;
        sheet.SetCell(new CellAddress(sheet.Id, farRow, 1), new NumberValue(999));

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Content at row 50,000 must be bold — it's an occupied cell within the selection
        var farCell = sheet.GetCell(farRow, 1);
        farCell.Should().NotBeNull();
        wb.GetStyle(farCell!.StyleId).Bold.Should().BeTrue(
            "a content cell at a far row must be styled even though it's beyond the typical used-range zone");
    }

    [Fact]
    public void WholeColumnBold_EmptyBeyondUsedRange_NoNewStyleOnlyEntries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Empty cells beyond the used range (row 11+) must NOT get style-only entries
        // This is the core perf fix: no 1M style-only entry creation
        sheet.GetStyleOnly(100, 1).Should().BeNull("empty cells beyond used range must not be styled");
        sheet.GetStyleOnly(1_000, 1).Should().BeNull("empty cells far beyond used range must not be styled");
        sheet.GetStyleOnly(CellAddress.MaxRow, 1).Should().BeNull("the last row must not be styled");
    }

    [Fact]
    public void WholeColumnBold_PreExistingStyleOnlyBeyondUsedRange_GetsUpdated()
    {
        // If a prior command already created a style-only entry beyond the used range,
        // a subsequent whole-column bold must update that entry (not silently skip it).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        // Simulate a pre-existing style-only entry at row 5000 (e.g. from a prior style command)
        const uint farRow = 5_000;
        var italicStyleId = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(farRow, 1, italicStyleId);

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // The pre-existing style-only at row 5000 must be updated to include bold
        var updatedStyleId = sheet.GetStyleOnly(farRow, 1);
        updatedStyleId.Should().NotBeNull("pre-existing style-only entries in the selection must be updated");
        var updatedStyle = wb.GetStyle(updatedStyleId!.Value);
        updatedStyle.Bold.Should().BeTrue("bold must be applied to the pre-existing style-only entry");
        updatedStyle.Italic.Should().BeTrue("the pre-existing italic must be preserved");
    }

    [Fact]
    public void WholeRowBold_With50UsedCols_StyleOnlyCountBoundedByUsedRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        const uint usedCols = 50;

        for (uint c = 1; c <= usedCols; c++)
            sheet.SetCell(new CellAddress(sheet.Id, 3, c), new NumberValue(c));

        var ctx = new TestCommandContext(wb);
        var wholeRow3 = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 3, CellAddress.MaxCol));

        var cmd = new ApplyStyleCommand(sheet.Id, wholeRow3, new StyleDiff(Bold: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        // No style-only entries since all 50 cells have content
        sheet.StyleOnlyCellCount.Should().Be(0,
            "all 50 cells have content — no style-only entries should be created");

        // All 50 content cells must be bold
        for (uint c = 1; c <= usedCols; c++)
            wb.GetStyle(sheet.GetCell(3, c)!.StyleId).Bold.Should().BeTrue($"col {c} must be bold");

        // Columns beyond the used range must not get style-only entries
        sheet.GetStyleOnly(3, CellAddress.MaxCol).Should().BeNull(
            "empty cells beyond used range in a whole-row selection must not be styled");
    }

    // ── HasStyleOnlyCells fast-path integrity ─────────────────────────────────

    [Fact]
    public void WholeColumnBold_ContentOnlySheet_HasStyleOnlyCellsRemainesFalse()
    {
        // HasStyleOnlyCells=true degrades the viewport fast path.  A whole-column bold on a
        // content-only sheet must not set it to true.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 100; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var ctx = new TestCommandContext(wb);
        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true)).Apply(ctx);

        sheet.HasStyleOnlyCells.Should().BeFalse(
            "bold on a content-only column must not create style-only entries that degrade the viewport fast path");
    }

    // ── Benchmark ────────────────────────────────────────────────────────────

    [BenchmarkFact]
    public void Benchmark_WholeColumnBold100UsedRows_ReportsTimingAndAllocations()
    {
        const int usedRows = 100;
        const int steps = 8;

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        for (uint r = 1; r <= usedRows; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var wholeColA = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        // Warmup
        var warmup = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();

        for (var i = 0; i < steps; i++)
        {
            var cmd = new ApplyStyleCommand(sheet.Id, wholeColA, new StyleDiff(Bold: true));
            var step = Stopwatch.StartNew();
            cmd.Apply(ctx).Success.Should().BeTrue();
            step.Stop();
            cmd.Revert(ctx);
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        // Assert allocations are bounded — each step touches usedRows cells, not MaxRow
        // Allow generous headroom: usedRows * BytesPerCell * steps * 10 is still tiny vs 200MB
        allocatedBytes.Should().BeLessThan(5_000_000,
            "whole-column bold on 100 used rows must not allocate ~200MB");

        Console.WriteLine(
            "PERF WHOLE_COLUMN_BOLD " +
            $"used_rows={usedRows} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }
}
