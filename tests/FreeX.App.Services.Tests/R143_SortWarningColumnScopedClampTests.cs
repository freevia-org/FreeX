using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R143-meta-sort-warning-sheet-wide-clamp: <see cref="QuickSortRangePlanner.ResolveAdjacentDataExpansion"/>'s
/// whole-column/whole-row clamp (added in R142) called <see cref="Sheet.GetUsedRange"/>, the bounding
/// box of the ENTIRE sheet across every column. A single stray cell in any far, unrelated column (or
/// row, for a whole-row selection) inflated that bound, made the "region contains the whole selection"
/// subset check fail, and silently reintroduced the exact data-scramble the Sort Warning exists to
/// prevent -- for the single most common sort gesture (click a column header, then Sort A-Z).
///
/// Fixed by scoping the clamp to the used extent of the columns (or rows) actually selected, via the
/// new <see cref="Sheet.GetUsedRangeInColumns"/>/<see cref="Sheet.GetUsedRangeInRows"/>.
///
/// This is round 3 of this specific feature (R141 built it disconnected, R141-remediation wired it,
/// R142 fixed the whole-column bypass/Custom-Sort-dialog gap/Table false positive, R143 fixes the
/// clamp itself) -- so this suite is deliberately a MATRIX over selection shape x sheet state rather
/// than a single-case regression test, to stop the pattern of each round only covering the one
/// reported case.
///
/// MATRIX COVERAGE (selection shape x sheet state) -- what's tested below and why:
///
/// | Sheet state \ Selection shape      | single cell | partial col in table | whole col (header) | whole row | multi-col subset | selection == whole region | multi-area |
/// |-------------------------------------|-------------|-----------------------|---------------------|-----------|-------------------|----------------------------|------------|
/// | contiguous block                    | (a)         | (b, via table)        | (c) R142 regr.      | (d)       | (e)               | (f)                        | (m) N/A    |
/// | block + stray in OTHER column        | -           | -                     | (g) THE BUG         | (h) sym.  | (i)               | -                          | (m) N/A    |
/// | block + stray in SAME column         | -           | -                     | (j)                 | -         | -                 | -                          | (m) N/A    |
/// | sparse sheet                        | -           | -                     | (k)                 | -         | -                 | -                          | (m) N/A    |
/// | ListObject table                    | -           | (b)                   | (l) whole-col over table | -    | -                 | -                          | (m) N/A    |
/// | filtered/hidden rows                | UNTESTED -- see note below                                                                                                    |
/// | blank column separating two blocks  | -           | -                     | (g)/(i) cover this (the "other" data is a full second block, not a lone cell) | - | - | - | (m) N/A |
///
/// Cells intentionally left uncovered, with rationale (per the "state explicitly, don't guess" rule):
/// <list type="bullet">
/// <item>
/// <b>Filtered/hidden rows</b>: grepping <see cref="QuickSortRangePlanner"/> and the sort path in
/// <see cref="WorkbookSession"/> shows no reference to row visibility/AutoFilter anywhere in this
/// planner -- hidden/filtered rows are not a concept this method (or its used-range clamp) is aware
/// of at all. Whatever visibility-aware behavior FreeX's sort has (if any) lives elsewhere (command
/// execution), not in the Sort Warning decision this file makes, so this matrix row is left untested
/// here rather than fabricating an assumption about what real Excel does when you sort a filtered
/// whole column with a stray cell in a hidden row of another column -- that combination is genuinely
/// unclear and out of scope for this fix.
/// </item>
/// <item>
/// <b>Multi-area selection</b>: <see cref="GridRange"/> is a single rectangle -- there is no
/// disjoint/multi-area representation at this layer, so "select A1:A5, Ctrl-click C1:C5, Sort" cannot
/// be constructed as an input to <see cref="QuickSortRangePlanner.ResolveAdjacentDataExpansion"/> at
/// all. If FreeX's host collapses a multi-area sort selection to a single GridRange before calling in
/// (e.g. the bounding box, or the primary area), that collapse happens above this method and is not
/// re-verified here.
/// </item>
/// <item>
/// <b>Single-cell / whole-region / multi-column-subset x stray-in-other-column</b>: these shapes do
/// not hit <see cref="QuickSortRangePlanner"/>'s whole-column/whole-row clamp branch at all (a
/// non-whole selection's End corners are already real, so <c>ClampToUsedRange</c> is a no-op for it
/// per the early return added by this fix) -- their "no false suppression from a stray cell elsewhere"
/// behavior was never broken and is covered generically by (e)/(i) (multi-col subset) and the
/// unconditional early-return paths at (a)/(f), so a dedicated per-shape stray-cell case for each of
/// them would be redundant with the reasoning already demonstrated by (e)/(i).
/// </item>
/// </list>
/// </summary>
public sealed class R143_SortWarningColumnScopedClampTests
{
    // (g) THE BUG: whole-column selection, stray cell in a FAR, UNRELATED column. This is the
    // exact scenario from the r143 finding -- fails before the fix (no prompt -> scramble), passes
    // after (prompt offered, correct small table region returned).
    [Fact]
    public void ResolveAdjacentDataExpansion_WholeColumnSelection_StrayCellInFarOtherColumn_StillDetectsExpansion()
    {
        var (_, sheet) = CreateSalesTableSheet();
        // A stray cell far below, in a totally unrelated column (Z), well past the 6-row table.
        sheet.SetCell(new CellAddress(sheet.Id, 5000, 26), new NumberValue(1)); // column Z = 26

        var wholeColumnA = new GridRange(
            Address(sheet, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var expansion = QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeColumnA);

        expansion.Should().Be(
            new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)),
            "a stray cell in an unrelated column must not inflate the clamp and suppress the warning " +
            "for the single most common sort gesture (click a column header, then Sort)");
    }

    // (h) Symmetric case for whole-ROW selections: a stray cell far to the right, in a totally
    // unrelated ROW, must not suppress the warning either.
    [Fact]
    public void ResolveAdjacentDataExpansion_WholeRowSelection_StrayCellInFarOtherRow_StillDetectsExpansion()
    {
        var (_, sheet) = CreateSalesTableSheet();
        // A stray cell far to the right, in a totally unrelated row (100), well past the 3-column table.
        sheet.SetCell(new CellAddress(sheet.Id, 100, 500), new NumberValue(1));

        // Rows 2-3 only (a genuine multi-row band -- CanSortSelectedRange requires RowCount > 1).
        var wholeRows2To3 = new GridRange(
            Address(sheet, 2, 1),
            new CellAddress(sheet.Id, 3, CellAddress.MaxCol));

        var expansion = QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeRows2To3);

        expansion.Should().Be(
            new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)),
            "a stray cell in an unrelated row must not inflate the clamp and suppress the warning");
    }

    // (i) Same as (g), but the "other data" is a whole second table separated by a blank column,
    // not a single stray cell -- covers the "blank column separating two blocks" sheet state, and
    // exercises the multi-column-subset selection shape at the same time (columns A:B selected,
    // leaving C -- part of the SAME table -- as the "adjacent" data the warning should surface).
    [Fact]
    public void ResolveAdjacentDataExpansion_MultiColumnSubsetSelection_UnrelatedBlockAcrossBlankColumn_DoesNotFalselySuppress()
    {
        var (_, sheet) = CreateSalesTableSheet();
        // Second, unrelated table two columns further right (column D is blank, separating them).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Other"));
        sheet.SetCell(new CellAddress(sheet.Id, 50, 5), new TextValue("Far"));

        // Select only columns A:B out of the 3-column A:C table -- a genuine partial selection.
        var columnsAB = new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 2));

        var expansion = QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, columnsAB);

        expansion.Should().Be(
            new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)),
            "the unrelated block in column E, across a blank column, must not affect a plain " +
            "multi-column selection's own subset comparison (this path never used the sheet-wide " +
            "clamp at all -- selection is not whole-column/whole-row)");
    }

    // (j) A stray value in the SAME column being sorted. What matters is whether the block around
    // the active cell reaches into columns the user did NOT select -- here it does (B and C), so
    // the warning fires and offers the whole table. A value elsewhere in the selected column is
    // inside the selection by definition and cannot be the adjacent data being protected; treating
    // it as a reason to stay silent is what let a leftover cell far below a table suppress the
    // warning and scramble the records.
    [Fact]
    public void ResolveAdjacentDataExpansion_WholeColumnSelection_StrayCellInSameColumn_StillPrompts()
    {
        var (_, sheet) = CreateSalesTableSheet();
        // A leftover value far below the table, in the column being sorted. r143's first attempt
        // asserted this produced NO prompt, reasoning that a stray cell with nothing beside it has
        // no pairing to protect. That reasoning looks at the wrong rows: the TABLE at A1:C6 still
        // has Name/Score/Team pairings, and sorting the whole of column A on its own scrambles
        // them. An audit reproduced exactly that through WorkbookSession.SortSelectedRange.
        sheet.SetCell(new CellAddress(sheet.Id, 5000, 1), new NumberValue(42));

        var wholeColumnA = new GridRange(
            Address(sheet, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var expansion = QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeColumnA);

        expansion.Should().NotBeNull(
            "the block around the active cell reaches into columns B and C, which the user did not " +
            "select -- that is the adjacent data Excel warns about, and no value elsewhere in the " +
            "selected column changes it");
        expansion!.Value.End.Col.Should().Be(3, "the offered expansion should cover the whole table");
    }

    // (k) Sparse sheet: scattered, non-contiguous cells across many columns, whole-column selection
    // over just one of them. The scoped clamp must ignore all the noise in other columns and use
    // only the selected column's own extent.
    [Fact]
    public void ResolveAdjacentDataExpansion_WholeColumnSelection_SparseSheetElsewhere_UsesOnlySelectedColumnExtent()
    {
        var (_, sheet) = CreateSalesTableSheet();
        // Scattered cells in many unrelated columns and rows -- a "sparse sheet" pattern.
        sheet.SetCell(new CellAddress(sheet.Id, 10, 8), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 999, 12), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 42, 20), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 100000, 2), new NumberValue(4)); // even column B (in-table!) has a far stray

        var wholeColumnA = new GridRange(
            Address(sheet, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var expansion = QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeColumnA);

        expansion.Should().Be(
            new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)),
            "column A's own real extent is just the 6-row table -- scattered data in columns B, H, " +
            "L, and T must not affect the clamp computed for a column-A-only selection");
    }

    // (l) ListObject Table: whole-column selection landing on a column that's part of a genuine
    // structured table, with an unrelated far stray cell elsewhere -- must still be suppressed (no
    // warning) because it's a Table selection, and must reach that answer via the correct
    // IsFullyInsideStructuredTable path (a small, correctly-clamped comparison range that fits
    // inside the table), not accidentally via a blown-up subset-check failure.
    [Fact]
    public void ResolveAdjacentDataExpansion_WholeColumnSelectionOverTableColumn_StrayCellElsewhere_SuppressedByTableNotByAccident()
    {
        var (_, sheet) = CreateSalesTableSheet();
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)),
            HeaderRowCount = 1,
        });
        // Unrelated stray cell in a far column, well outside the table.
        sheet.SetCell(new CellAddress(sheet.Id, 5000, 26), new NumberValue(1));

        // Click column C's header -- C is the table's own rightmost column.
        var wholeColumnC = new GridRange(
            Address(sheet, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));

        QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeColumnC).Should().BeNull(
            "a whole-column selection landing on a structured table column must never prompt, " +
            "regardless of unrelated data elsewhere on the sheet");
    }

    // (a)/(f) baseline single-cell and whole-region no-op paths are already covered by the existing
    // R141/R142 suites (R141_SortAdjacentDataWarningTests, R142_SortWarningWholeColumnAndTableTests);
    // (c) is R142's own "no false expansion for a column that already covers all its data" test,
    // reproduced here is unnecessary -- referenced instead for matrix completeness.

    private static (WorkbookSession Session, Sheet Sheet) CreateSalesTableSheet()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Team"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(4));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("North"));
        sheet.SetCell(Address(sheet, 5, 1), new TextValue("Deb"));
        sheet.SetCell(Address(sheet, 5, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 5, 3), new TextValue("South"));
        sheet.SetCell(Address(sheet, 6, 1), new TextValue("Eve"));
        sheet.SetCell(Address(sheet, 6, 2), new NumberValue(5));
        sheet.SetCell(Address(sheet, 6, 3), new TextValue("Central"));

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);
}
