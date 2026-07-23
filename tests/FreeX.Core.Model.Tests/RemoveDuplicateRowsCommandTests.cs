using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class RemoveDuplicateRowsCommandTests
{
    [Fact]
    public void RemoveDuplicateRowsCommand_RemovesDuplicateRowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();

        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("C"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("C"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_UsesSelectedColumnOffsetsOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Ben"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(10));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range, [0u, 2u]);

        command.Apply(ctx).Success.Should().BeTrue();

        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Ada"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("South"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Ada"));
        sheet.GetValue(3, 1).Should().BeOfType<BlankValue>();

        command.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new TextValue("Ada"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Ben"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Ada"));
    }

    [Fact]
    public void CompositeWorkbookCommand_RemovesDuplicateRowsAcrossGroupedSheetsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        SeedDuplicateRows(sheet1, "A", "B", "A", "C");
        SeedDuplicateRows(sheet2, "North", "South", "North", "West");
        var range1 = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 4, 1));
        var range2 = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 4, 1));
        var command = new CompositeWorkbookCommand(
            "Remove Duplicates",
            [
                new RemoveDuplicateRowsCommand(sheet1.Id, range1),
                new RemoveDuplicateRowsCommand(sheet2.Id, range2)
            ]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet1.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet1.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet1.GetValue(3, 1).Should().Be(new TextValue("C"));
        sheet2.GetValue(1, 1).Should().Be(new TextValue("North"));
        sheet2.GetValue(2, 1).Should().Be(new TextValue("South"));
        sheet2.GetValue(3, 1).Should().Be(new TextValue("West"));

        command.Revert(ctx);

        sheet1.GetValue(3, 1).Should().Be(new TextValue("A"));
        sheet1.GetValue(4, 1).Should().Be(new TextValue("C"));
        sheet2.GetValue(3, 1).Should().Be(new TextValue("North"));
        sheet2.GetValue(4, 1).Should().Be(new TextValue("West"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
    }

    // ── New tests covering range-scope fix and key collision fix ─────────────

    [Fact]
    public void RemoveDuplicateRowsCommand_DataOutsideRangeColumnsIsNotTouched()
    {
        // Arrange: columns A-B in range, column D outside range.
        // Row 3 is a duplicate of row 1 in the A-B range.
        // Column D on row 3 must survive; rows below the range must not shift.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("D1-outside"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("D2-outside"));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alpha")); // duplicate of row 1
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("D3-outside"));

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("BelowRange"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2)); // A1:B3

        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        // Act
        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        // In-range: Alpha/1 and Beta/2 compacted into rows 1-2; row 3 in-range columns cleared.
        sheet.GetValue(1, 1).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Beta"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().BeOfType<BlankValue>("vacated in-range row cleared");
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>("vacated in-range row cleared");

        // Out-of-range column D must be completely untouched on ALL rows.
        sheet.GetValue(1, 4).Should().Be(new TextValue("D1-outside"));
        sheet.GetValue(2, 4).Should().Be(new TextValue("D2-outside"));
        sheet.GetValue(3, 4).Should().Be(new TextValue("D3-outside"), "column D on duplicate row must not be deleted");

        // Row below the range must not shift.
        sheet.GetValue(5, 1).Should().Be(new TextValue("BelowRange"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_UndoRestoresExactPriorState()
    {
        // Apply then Revert — every cell (including out-of-range) must be identical to before.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X")); // duplicate
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("C1-outside"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("C2-outside"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("C3-outside"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)); // A1:A3

        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);
        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("X"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("X"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Y"));

        // Out-of-range column C must also be unaffected throughout.
        sheet.GetValue(1, 3).Should().Be(new TextValue("C1-outside"));
        sheet.GetValue(2, 3).Should().Be(new TextValue("C2-outside"));
        sheet.GetValue(3, 3).Should().Be(new TextValue("C3-outside"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_UndoRestoresStyleOnlyOnBlankCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Row 1: a blank cell with a style-only entry (formatting without a value).
        var styleId = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(1, 1, styleId);                                  // A1: blank + bold
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Z")); // A2: value
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Z")); // A3: duplicate of A2

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)); // A1:A3

        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);
        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        command.Revert(ctx);

        // The style-only entry on A1 must be restored.
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1)).Should().BeNull("blank cell should have no Cell object");
        sheet.GetStyleOnly(1, 1).Should().Be(styleId, "style-only entry must be restored by Revert");
        sheet.GetValue(2, 1).Should().Be(new TextValue("Z"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Z"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_CommentsAndHyperlinksMovedWithSurvivingRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var addr11 = new CellAddress(sheet.Id, 1, 1);
        var addr21 = new CellAddress(sheet.Id, 2, 1);
        var addr31 = new CellAddress(sheet.Id, 3, 1);

        sheet.SetCell(addr11, new TextValue("Foo"));
        sheet.Comments[addr11] = "Note on row 1";
        sheet.Hyperlinks[addr11] = "https://example.com/1";

        sheet.SetCell(addr21, new TextValue("Foo")); // duplicate of row 1, will be removed
        sheet.Comments[addr21] = "Note on row 2 (duplicate)";

        sheet.SetCell(addr31, new TextValue("Bar")); // survivor, compacted to row 2
        sheet.Comments[addr31] = "Note on row 3";
        sheet.Hyperlinks[addr31] = "https://example.com/3";

        var range = new GridRange(addr11, addr31);
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        // Row 1 kept in place: comment + hyperlink survive.
        sheet.GetValue(1, 1).Should().Be(new TextValue("Foo"));
        sheet.Comments.Should().ContainKey(addr11).WhoseValue.Should().Be("Note on row 1");
        sheet.Hyperlinks.Should().ContainKey(addr11).WhoseValue.Should().Be("https://example.com/1");

        // Row 3 ("Bar") compacted to row 2: comment + hyperlink must move.
        var addr21After = new CellAddress(sheet.Id, 2, 1);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Bar"));
        sheet.Comments.Should().ContainKey(addr21After).WhoseValue.Should().Be("Note on row 3");
        sheet.Hyperlinks.Should().ContainKey(addr21After).WhoseValue.Should().Be("https://example.com/3");

        // Old row 3 must be cleared.
        sheet.GetValue(3, 1).Should().BeOfType<BlankValue>();
        sheet.Comments.Should().NotContainKey(addr31);
        sheet.Hyperlinks.Should().NotContainKey(addr31);

        // Undo
        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Foo"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Foo"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Bar"));
        sheet.Comments.Should().ContainKey(addr11).WhoseValue.Should().Be("Note on row 1");
        sheet.Comments.Should().ContainKey(addr21).WhoseValue.Should().Be("Note on row 2 (duplicate)");
        sheet.Comments.Should().ContainKey(addr31).WhoseValue.Should().Be("Note on row 3");
        sheet.Hyperlinks.Should().ContainKey(addr11).WhoseValue.Should().Be("https://example.com/1");
        sheet.Hyperlinks.Should().NotContainKey(addr21, "row 2 had no hyperlink before Apply");
        sheet.Hyperlinks.Should().ContainKey(addr31).WhoseValue.Should().Be("https://example.com/3");
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_TabInValueDoesNotCauseSpuriousDedup()
    {
        // ("a\tb", "c") must NOT be treated as equal to ("a", "b\tc").
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a\tb"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("c"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("a"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("b\tc"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();

        // Both rows are distinct — nothing should be removed.
        command.RemovedRowCount.Should().Be(0);
        sheet.GetValue(1, 1).Should().Be(new TextValue("a\tb"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("c"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("a"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("b\tc"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_ColumnOffsetVariantPreservesCompaction()
    {
        // Key on column offsets 0 (col A) only within a 3-col range.
        // Row 2 duplicates row 1 on col A, but has unique col B — it is removed.
        // Data compacts correctly; undo restores.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("First"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Dup")); // duplicate key
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Second"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Unique"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Third"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range, [0u]); // key on col A only

        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Dup"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("First"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Unique"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Third"));
        sheet.GetValue(3, 1).Should().BeOfType<BlankValue>("vacated row cleared in range");
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>("vacated row cleared in range");

        command.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new TextValue("First"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Second"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Third"));
    }

    // ── Phonetic guide (furigana) carried through compaction/undo (R79-commands-undo-redo-5-2) ──

    [Fact]
    public void RemoveDuplicateRowsCommand_PhoneticGuideMovesWithSurvivingRowAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var addr11 = new CellAddress(sheet.Id, 1, 1);
        var addr21 = new CellAddress(sheet.Id, 2, 1);
        var addr31 = new CellAddress(sheet.Id, 3, 1);

        var guideRow1 = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>たなか</t></rPh>"], null);
        var guideRow2 = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>duplicate-guide</t></rPh>"], null);
        var guideRow3 = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>すずき</t></rPh>"], null);

        sheet.SetCell(addr11, new TextValue("田中"));
        sheet.CellPhoneticGuides[addr11] = guideRow1;

        sheet.SetCell(addr21, new TextValue("田中")); // duplicate of row 1, will be removed
        sheet.CellPhoneticGuides[addr21] = guideRow2;

        sheet.SetCell(addr31, new TextValue("鈴木")); // survivor, compacted to row 2
        sheet.CellPhoneticGuides[addr31] = guideRow3;

        var range = new GridRange(addr11, addr31);
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        // Row 1 kept in place: its own guide survives.
        sheet.CellPhoneticGuides.Should().ContainKey(addr11).WhoseValue.Should().BeSameAs(guideRow1);

        // Row 3 ("鈴木") compacted to row 2: its guide must travel with it, not the stale
        // duplicate-row guide that used to sit at row 2.
        var addr21After = new CellAddress(sheet.Id, 2, 1);
        sheet.GetValue(2, 1).Should().Be(new TextValue("鈴木"));
        sheet.CellPhoneticGuides.Should().ContainKey(addr21After).WhoseValue.Should().BeSameAs(guideRow3);

        // Old row 3 (vacated) must be cleared of its guide.
        sheet.CellPhoneticGuides.Should().NotContainKey(addr31);

        // Undo
        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("田中"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("田中"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("鈴木"));
        sheet.CellPhoneticGuides.Should().ContainKey(addr11).WhoseValue.Should().BeSameAs(guideRow1);
        sheet.CellPhoneticGuides.Should().ContainKey(addr21).WhoseValue.Should().BeSameAs(guideRow2);
        sheet.CellPhoneticGuides.Should().ContainKey(addr31).WhoseValue.Should().BeSameAs(guideRow3);
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_NoPhoneticGuideOnSurvivorClearsStaleTargetGuideAndUndoRestores()
    {
        // No-regression sibling: the surviving row that compacts into a target address has NO
        // guide of its own, but that target address previously held a different row's guide
        // (which is being cleared as part of the in-range clear). The target must end up with no
        // guide at all — not the leftover from whatever used to occupy that address — and Undo
        // must restore the original guide back at its original address.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var addr11 = new CellAddress(sheet.Id, 1, 1);
        var addr21 = new CellAddress(sheet.Id, 2, 1);
        var addr31 = new CellAddress(sheet.Id, 3, 1);

        var guideRow2 = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>guide-at-row2</t></rPh>"], null);

        sheet.SetCell(addr11, new TextValue("Dup"));
        sheet.SetCell(addr21, new TextValue("Dup")); // duplicate of row 1, will be removed
        sheet.CellPhoneticGuides[addr21] = guideRow2;

        sheet.SetCell(addr31, new TextValue("Unique")); // survivor, compacted to row 2, no guide of its own
        // No guide set on row 3.

        var range = new GridRange(addr11, addr31);
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        // Row 2 (compacted from row 3) must have NO phonetic guide — not the stale one that used
        // to live at row 2 before the clear.
        var addr21After = new CellAddress(sheet.Id, 2, 1);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Unique"));
        sheet.CellPhoneticGuides.Should().NotContainKey(addr21After);

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Dup"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Dup"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Unique"));
        sheet.CellPhoneticGuides.Should().ContainKey(addr21).WhoseValue.Should().BeSameAs(guideRow2);
        sheet.CellPhoneticGuides.Should().NotContainKey(addr11);
        sheet.CellPhoneticGuides.Should().NotContainKey(addr31);
    }

    private static void SeedDuplicateRows(Sheet sheet, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)index + 1, 1), new TextValue(values[index]));
    }

    // ── Snapshot-before-detect guard (P3 regression) ─────────────────────────

    [Fact]
    public void RemoveDuplicateRowsCommand_NoDuplicates_ReturnsSuccessWithoutModifyingSheet()
    {
        // When no duplicates are found the command must return success as a no-op and must NOT
        // have materialized a full-range snapshot (the fix: detect first, snapshot only when needed).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gamma"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue("no-op when no duplicates must still report success");
        command.RemovedRowCount.Should().Be(0, "no rows removed when all unique");

        // Sheet must be completely unmodified
        sheet.GetValue(1, 1).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Beta"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Gamma"));

        // Revert on a no-op must also be safe
        command.Revert(ctx);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Alpha"), "revert of no-op leaves sheet unchanged");
        sheet.GetValue(2, 1).Should().Be(new TextValue("Beta"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Gamma"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_NoDuplicates_AffectedCellsIsEmpty()
    {
        // AffectedCells must be null/empty for a no-op — callers that trigger recalc based on
        // AffectedCells must not recalc the full range unnecessarily.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        for (uint r = 1; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new TextValue($"Unique{r}"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // No affected cells on a no-op
        (outcome.AffectedCells is null || outcome.AffectedCells.Count == 0).Should().BeTrue(
            "no-op remove-duplicates must not report affected cells");
    }

}
