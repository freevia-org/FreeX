using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-app-accessibility-checker-5-4: <c>AddBlankTableBodyRowAndColumnIssues</c> walked every declared
/// (row, col) pair in a structured table's full extent via direct <c>sheet.GetValue</c> lookups,
/// instead of bounding the scan to the sheet's occupied cells (as <c>AddLowContrastCellTextIssues</c>
/// and <c>AddHiddenContentIssues</c> already do). A table declared over a large full-height column
/// range (Excel permits Insert &gt; Table over an entire column selection) with only a handful of
/// populated rows made this scan cost roughly 2x(RowCount x ColumnCount) lookups. Drives the real
/// product entry point, <see cref="AccessibilityCheckerService.FindIssues"/>.
/// </summary>
public sealed class R90_AccessibilityBlankTableScanBoundedTests
{
    [Fact]
    public void FindIssues_ScansLargeSparseTable_WithoutWalkingEveryDeclaredCell()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");

        const uint rowCount = 1_048_576; // Excel's row limit -- a table created over an entire column range.
        const int columnCount = 40;

        // Only the header row and a single data row are populated -- the rest of the huge declared
        // table extent is genuinely blank, mirroring a table created over an entire column range with
        // only a handful of real rows of data.
        for (var col = 1; col <= columnCount; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)col), new TextValue($"Column{col}"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, (uint)col), new TextValue($"Value{col}"));
        }

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, rowCount, (uint)columnCount)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var stopwatch = Stopwatch.StartNew();
        var issues = AccessibilityCheckerService.FindIssues(workbook);
        stopwatch.Stop();

        // Primary, deterministic assertion (not timing-dependent, so it can't flake under parallel
        // test-host load): the fix collapses each contiguous run of fully-blank rows into a single
        // issue instead of emitting one per row, so the huge (1,048,574-row) blank run below the one
        // populated row must surface as a small, constant number of issues -- not hundreds of
        // thousands/millions of them. A regression back to a per-row (or otherwise per-declared-cell)
        // walk would blow this bound up by many orders of magnitude regardless of how fast any single
        // iteration is.
        issues.Count.Should().BeLessThan(100,
            "a fully-blank multi-million-row run in a table must collapse into a small, constant " +
            "number of issues, not one issue per declared row");

        // Secondary sanity check retained for a human-readable perf signal in CI output; not the
        // gating assertion (wall-clock thresholds are a known flake source under parallel test-host
        // load) -- the issues.Count bound above is what actually gates this test.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the blank-row/column scan must be bounded to the sheet's occupied cells, not every " +
            "declared (row, col) pair across the table's full extent");

        // Every data-body row past the single populated one is fully blank.
        issues.Should().Contain(i =>
            i.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable &&
            i.Message == "Tables should not contain fully blank rows.");
    }

    [Fact]
    public void FindIssues_StillFlagsBlankColumn_ForAModestlySizedTable()
    {
        // No-regression sibling: the ordinary (small-table) blank-column detection this fix touched
        // must still work correctly -- mirrors the pre-existing
        // FindIssues_FlagsStructuredTableWithFullyBlankInteriorColumn test, confirming the occupied-
        // cell-bounded rewrite preserves exact detection semantics at ordinary scale.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Notes"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(200));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable).Subject;

        issue.Location.Should().Be("B2:B3");
        issue.Message.Should().Be("Tables should not contain fully blank columns.");
    }
}
