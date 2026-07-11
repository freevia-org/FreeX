using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round-27 SortCommand fixes:
///
/// R27-sort-deep-2: mixed blank + error cells were not ordered relative to each other — Excel's
/// fixed sort precedence (numbers, text, logicals, errors, then blanks always last) requires
/// errors to sort above blanks within the "goes last" bucket, but SortCommand previously merged
/// both kinds into a single tied bucket and fell through to the stable original-index tiebreak.
///
/// R27-sort-deep-3: a Cell/Font-color sort with no explicit target color chosen inverted the
/// "no-fill goes last" rule when the sort direction was descending, because CompareNullableColor's
/// absolute (non-direction-aware) null-vs-color ordering was run through the same
/// ascending/descending negation applied to real value comparisons.
///
/// R27-protection-eval-deep-2: Sort permission alone allowed sorting a range that contains locked
/// cells on a protected sheet; real Excel blocks Sort on any such range regardless of the
/// permission checkbox — the checkbox only ever matters for a range that is entirely unlocked.
///
/// (R27-sort-deep-1, "Sort on a filtered range moves filter-hidden row data", was investigated and
/// found to be a false positive: SortCommand's existing behavior of permuting filter-hidden row
/// data — and its FilterHiddenRows/ValueFilterHiddenRows/ColumnFilterOwnedRows bookkeeping — in
/// lockstep with the sort is the deliberate, previously-verified fix for findings H1/R21-autofilter-
/// sort-state-1, matching real Excel's actual AutoFilter behavior: sorting a filtered list re-sorts
/// the WHOLE underlying list (visible and hidden rows alike), then the filter re-evaluates against
/// the (unchanged) criteria — which is exactly equivalent to "the hidden flag follows the data".
/// No code change was made for that finding.)
/// </summary>
public sealed class R27_SortCommandDeepTests
{
    // ── R27-sort-deep-2: errors sort above blanks within the "goes last" bucket ────────────────

    [Fact]
    public void AscendingSort_OrdersErrorsAboveBlanksWithinTheGoesLastBucket()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        // A1=100, A2=<blank>, A3=#VALUE!, A4=50.
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(100));
        // A2 intentionally left blank (no cell set).
        sheet.SetCell(new CellAddress(sid, 3, 1), ErrorValue.Value);
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(50));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));
        var command = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel order: numbers ascending, then the error, then the blank (blank strictly last).
        sheet.GetValue(1, 1).Should().Be(new NumberValue(50));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(100));
        sheet.GetValue(3, 1).Should().Be(ErrorValue.Value);
        sheet.GetCell(4, 1).Should().BeNull("the blank cell must still sort strictly last, after the error");
    }

    [Fact]
    public void AscendingSort_StillOrdersBlanksLastWhenNoErrorsArePresent()
    {
        // Sibling no-regression: the ordinary blank-goes-last case (no errors involved at all)
        // must still work exactly as before.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(30));
        // A2 intentionally left blank.
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(10));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 1));
        var command = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(30));
        sheet.GetCell(3, 1).Should().BeNull("the blank cell must still sort last with no errors in the range");
    }

    // ── R27-sort-deep-3: "no fill" always sorts last, regardless of direction ──────────────────

    [Fact]
    public void CellColorDescendingWithNoTargetColor_KeepsNoFillLast()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });

        // Row 1 has no fill, row 2 has a red fill — the opposite of the desired final order, so a
        // wrongly-inverted "descending" sort would leave them exactly where they are.
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 2));
        SetStyledRow(sheet, sid, 1, "NoFill", StyleId.Default);
        SetStyledRow(sheet, sid, 2, "Red", redStyle);

        // Sort On: Cell Color, Order: On Bottom (descending), with no specific color chosen.
        var command = new SortCommand(sid, range, [new SortKey(0, Ascending: false, SortOn.CellColor)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // The no-fill row must still land last, regardless of the descending direction.
        sheet.GetValue(1, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("NoFill"));
    }

    [Fact]
    public void CellColorAscendingWithNoTargetColor_KeepsNoFillLast()
    {
        // Sibling no-regression: the ascending direction already kept "no fill" last before the
        // fix (CompareNullableColor's un-negated result already encodes that ordering); verify it
        // still holds with the same setup as the descending case above.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 2));
        SetStyledRow(sheet, sid, 1, "NoFill", StyleId.Default);
        SetStyledRow(sheet, sid, 2, "Red", redStyle);

        var command = new SortCommand(sid, range, [new SortKey(0, Ascending: true, SortOn.CellColor)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("NoFill"));
    }

    // ── R27-protection-eval-deep-2: locked cells block Sort even with the Sort permission ──────

    [Fact]
    public void ProtectedSheetWithSortPermission_StillRejectsRangeContainingLockedCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        // Cells default to Locked=true, matching a freshly-protected sheet where the user never
        // explicitly unlocked the range.
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.Sort);

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1));
        var command = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse("Excel blocks Sort on locked cells regardless of the Sort permission checkbox");
        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void ProtectedSheetWithSortPermission_AllowsRangeExplicitlyUnlocked()
    {
        // Sibling no-regression: the Sort permission must still work once the range is explicitly
        // unlocked (Format Cells > Protection > Locked unchecked) — the intended, documented case.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false });

        var cellA = Cell.FromValue(new NumberValue(2));
        cellA.StyleId = unlockedStyle;
        sheet.SetCell(new CellAddress(sid, 1, 1), cellA);
        var cellB = Cell.FromValue(new NumberValue(1));
        cellB.StyleId = unlockedStyle;
        sheet.SetCell(new CellAddress(sid, 2, 1), cellB);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.Sort);

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1));
        var command = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
    }

    private static void SetStyledRow(Sheet sheet, SheetId sheetId, uint row, string label, StyleId style)
    {
        // Column 1 (the sort key) carries the style being sorted on; column 2 is a plain,
        // unstyled copy of the same label used purely to verify which row ended up where.
        var keyCell = Cell.FromValue(new TextValue(label));
        keyCell.StyleId = style;
        sheet.SetCell(new CellAddress(sheetId, row, 1), keyCell);
        sheet.SetCell(new CellAddress(sheetId, row, 2), new TextValue(label));
    }
}
