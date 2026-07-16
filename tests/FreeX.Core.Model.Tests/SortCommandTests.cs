using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SortCommandTests
{
    [Fact]
    public void SortCommand_SupportsMultipleSortKeysAndUndoRestoresRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(15));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true), new SortKey(1, false)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(15));
        sheet.GetValue(2, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(5));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void SortCommand_SupportsCaseSensitiveTextOrder()
    {
        // R39-commands-sort-custom-2-1: Excel's case-sensitive sort is still alphabetical first —
        // case only breaks a tie between letter-identical strings, and lowercase sorts before
        // uppercase in that tiebreak. "apple" and "Banana" differ by their first letter (a < b),
        // so alphabetical order puts "apple" first regardless of case sensitivity — the previous
        // version of this test wrongly asserted the opposite (raw codepoint/ordinal order, which
        // clumps "Banana" ahead of "apple" purely because 'B' < 'a' as UTF-16 code units).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(CaseSensitive: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("apple"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Banana"));
    }

    [Fact]
    public void SortCommand_LeftToRight_SortsColumnsByRowKeyAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("B"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 2).Should().Be(new TextValue("B"));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 3).Should().Be(new TextValue("C"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 1).Should().Be(new TextValue("C"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 2).Should().Be(new TextValue("A"));
    }

    [Fact]
    public void SortCommand_LeftToRight_MovesCommentsAndThreadedCommentsAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3));
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(a1, new NumberValue(3));
        sheet.SetCell(a2, new TextValue("C"));
        sheet.SetCell(b1, new NumberValue(1));
        sheet.SetCell(b2, new TextValue("A"));
        sheet.SetCell(c1, new NumberValue(2));
        sheet.SetCell(c2, new TextValue("B"));
        sheet.Comments[a2] = "comment C";
        sheet.Comments[b2] = "comment A";
        sheet.Comments[c2] = "comment B";
        sheet.ThreadedComments[a1] = new ThreadedComment("thread C", "Quinn");
        sheet.ThreadedComments[b1] = new ThreadedComment("thread A", "Codex");
        sheet.ThreadedComments[c1] = new ThreadedComment("thread B", "FreeX");

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[a2].Should().Be("comment A");
        sheet.Comments[b2].Should().Be("comment B");
        sheet.Comments[c2].Should().Be("comment C");
        sheet.ThreadedComments[a1].Should().Be(new ThreadedComment("thread A", "Codex"));
        sheet.ThreadedComments[b1].Should().Be(new ThreadedComment("thread B", "FreeX"));
        sheet.ThreadedComments[c1].Should().Be(new ThreadedComment("thread C", "Quinn"));

        command.Revert(ctx);

        sheet.Comments[a2].Should().Be("comment C");
        sheet.Comments[b2].Should().Be("comment A");
        sheet.Comments[c2].Should().Be("comment B");
        sheet.ThreadedComments[a1].Should().Be(new ThreadedComment("thread C", "Quinn"));
        sheet.ThreadedComments[b1].Should().Be(new ThreadedComment("thread A", "Codex"));
        sheet.ThreadedComments[c1].Should().Be(new ThreadedComment("thread B", "FreeX"));
    }

    [Fact]
    public void SortCommand_CommandBusUndoReportsSortedRangeAffectedCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new TestCommandContext(workbook));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("B"));
        var expectedAffectedCells = range.AllCells().ToList();

        var execute = bus.Execute(workbook.Id, new SortCommand(sheet.Id, range, [new SortKey(0, true)]));
        var undo = bus.Undo(workbook.Id);
        var redo = bus.Redo(workbook.Id);

        execute.AffectedCells.Should().Equal(expectedAffectedCells);
        undo.AffectedCells.Should().Equal(expectedAffectedCells);
        redo.AffectedCells.Should().Equal(expectedAffectedCells);
    }

    [Fact]
    public void SortCommand_UndoRestoresOnlySortedRowMetadata()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.RowHeights[2] = 50;
        sheet.RowHeights[99] = 99;
        sheet.HiddenRows.Add(2);
        sheet.HiddenRows.Add(99);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);
        command.Apply(ctx);
        sheet.RowHeights[99] = 123;
        sheet.HiddenRows.Remove(99);
        sheet.HiddenRows.Add(100);

        command.Revert(ctx);

        sheet.RowHeights.Should().ContainKey(2).WhoseValue.Should().Be(50);
        sheet.HiddenRows.Should().Contain(2);
        sheet.RowHeights.Should().ContainKey(99).WhoseValue.Should().Be(123);
        sheet.HiddenRows.Should().NotContain(99);
        sheet.HiddenRows.Should().Contain(100);
    }

    [Fact]
    public void SortCommand_WithActiveAutoFilter_FilterHiddenRowStaysPinnedAtItsOwnRowAndUndo()
    {
        // R45-commands-sort-filter-interaction-3-1: real Excel documents that hidden rows in a
        // filtered range are NOT sorted — a row the active AutoFilter is hiding must stay at its
        // own physical row, completely untouched, while only the VISIBLE rows are reordered among
        // themselves. This supersedes the prior (buggy) expectation that the hidden flag simply
        // "follows" a row's data to a new position: real Excel never relocates that row's data at
        // all, so there is nothing for a hidden flag to follow.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Header + 3 data rows; filter column keeps "Keep" and hides "Drop".
        // "Drop" sits at row 2 with numeric key 3, the largest — if it wrongly participated in an
        // ascending sort it would move down to row 4; the fix keeps it pinned at row 2 instead.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(2));

        var filterRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        new FilterCommand(sheet.Id, filterRange, 0, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]); // row 2 ("Drop") is hidden

        // Sort ascending by the numeric column over the whole visible range (rows 2-4). Row 2
        // ("Drop") is filter-hidden and must never move or be compared; only the two visible
        // "Keep" rows (5 and 2) are reordered between themselves, landing in rows 3 and 4 — the
        // exact two physical slots that were visible before the sort.
        var sortRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 2));
        var sortCommand = new SortCommand(sheet.Id, sortRange, [new SortKey(1, true)]);
        sortCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(2, 1).Should().Be(new TextValue("Drop"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(3));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Keep"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Keep"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(5));

        // The hidden flag must still be exactly on row 2 — the filter-hidden row never moved.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);

        sortCommand.Revert(ctx);

        // After undo, the original arrangement (and its filter-hidden row) must be restored.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Drop"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(3));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Keep"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Keep"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(2));
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
    }

    [Fact]
    public void SortCommand_CanSortRowsByCellFillColor()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var blueStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 0, 255) });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue", blueStyle);
        SetStyledRow(sheet, 3, "Red", redStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true, SortOn.CellColor)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Blue"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Plain"));
    }

    [Fact]
    public void SortCommand_CellFillColorTarget_MovesSelectedColorToTop()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var red = new CellColor(255, 0, 0);
        var blueStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 0, 255) });
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = red });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue", blueStyle);
        SetStyledRow(sheet, 3, "Red 1", redStyle);
        SetStyledRow(sheet, 4, "Red 2", redStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true, SortOn.CellColor, red)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Red 1"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Red 2"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Plain"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("Blue"));
    }

    [Fact]
    public void SortCommand_CanSortRowsByFontColorDescending()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var redStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(255, 0, 0) });
        var blueStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue", blueStyle);
        SetStyledRow(sheet, 3, "Red", redStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, false, SortOn.FontColor)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Blue"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Plain"));
    }

    [Fact]
    public void SortCommand_FontColorTarget_MovesSelectedColorToBottom()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var blue = new CellColor(0, 0, 255);
        var redStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(255, 0, 0) });
        var blueStyle = workbook.RegisterStyle(new CellStyle { FontColor = blue });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue 1", blueStyle);
        SetStyledRow(sheet, 3, "Red", redStyle);
        SetStyledRow(sheet, 4, "Blue 2", blueStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, false, SortOn.FontColor, blue)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Plain"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Blue 1"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("Blue 2"));
    }

    [Fact]
    public void SortCommand_RejectsProtectedSheetWithoutSortPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.IsProtected = true;

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void SortCommand_AllowsProtectedSheetWithSortPermission()
    {
        // R27-protection-eval-deep-2: real Excel still blocks Sort on any range containing
        // locked cells on a protected sheet regardless of the Sort permission checkbox — the
        // range must be explicitly unlocked (Format Cells > Protection > Locked unchecked) for
        // the permission grant to actually allow sorting it.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false });
        var cellA = Cell.FromValue(new NumberValue(2));
        cellA.StyleId = unlockedStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cellA);
        var cellB = Cell.FromValue(new NumberValue(1));
        cellB.StyleId = unlockedStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), cellB);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.Sort);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void SortCommand_RejectsProtectedSheetWithSortPermissionButLockedCells()
    {
        // R27-protection-eval-deep-2: even with the Sort permission granted, Excel still blocks
        // sorting a range that contains a locked cell on a protected sheet (cells default to
        // Locked=true), unlike the sibling case above where the range was explicitly unlocked.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.Sort);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void SortCommand_AppliesCustomListOrderToFirstKeyAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        // Rows out of calendar order; alphabetical would give Apr, Feb, Jan, Mar.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Apr"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(2));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        CustomSortOrder.TryParse("Jan, Feb, Mar, Apr", out var order).Should().BeTrue();
        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, true, CustomOrder: order)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Jan"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Feb"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Mar"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Apr"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Mar"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Jan"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Apr"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Feb"));
    }

    [Fact]
    public void SortCommand_CustomListOrderRespectsDescendingDirection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Mar"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        CustomSortOrder.TryParse("Jan, Feb, Mar", out var order).Should().BeTrue();
        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, false, CustomOrder: order)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Mar"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Feb"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Jan"));
    }

    [Fact]
    public void SortCommand_CustomListOrder_PlacesNonListValuesLast()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Zebra"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Jan"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        CustomSortOrder.TryParse("Jan, Feb, Mar", out var order).Should().BeTrue();
        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, true, CustomOrder: order)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Jan"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Feb"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Zebra"));
    }

    [Fact]
    public void SortCommand_HyperlinkRidesItsRow_AndUndoRestoresOriginalPosition()
    {
        // A hyperlink attached to row 2 col 1 must move to row 1 col 1 after sort asc,
        // and return to row 2 col 1 after undo.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var r1c1 = new CellAddress(sheet.Id, 1, 1);
        var r2c1 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(r1c1, new NumberValue(2));
        sheet.SetCell(r2c1, new NumberValue(1));

        // Attach hyperlink to row 2 (value=1, will sort to row 1)
        var meta = new HyperlinkMetadata(ScreenTip: "Go to example");
        sheet.Hyperlinks[r2c1] = "https://example.com";
        sheet.HyperlinkMetadata[r2c1] = meta;

        var range = new GridRange(r1c1, r2c1);
        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // After sort: value 1 (originally row 2) is now at row 1
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        // Hyperlink must have followed its cell to row 1
        sheet.Hyperlinks.Should().ContainKey(r1c1).WhoseValue.Should().Be("https://example.com");
        sheet.HyperlinkMetadata.Should().ContainKey(r1c1).WhoseValue.Should().Be(meta);
        // Row 2 now holds value 2 with no hyperlink
        sheet.Hyperlinks.Should().NotContainKey(r2c1);

        command.Revert(ctx);

        // After undo: hyperlink is back at its original row 2
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
        sheet.Hyperlinks.Should().ContainKey(r2c1).WhoseValue.Should().Be("https://example.com");
        sheet.HyperlinkMetadata.Should().ContainKey(r2c1).WhoseValue.Should().Be(meta);
        sheet.Hyperlinks.Should().NotContainKey(r1c1);
    }

    [Fact]
    public void SortCommand_StyleOnlyRidesItsRow_AndUndoRestoresOriginalEntries()
    {
        // A blank cell with a style-only fill at row 2 must move with its row after sort,
        // and the style-only entry must be restored at the original address after undo.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var r1c1 = new CellAddress(sheet.Id, 1, 1);
        var r2c1 = new CellAddress(sheet.Id, 2, 1);
        var r1c2 = new CellAddress(sheet.Id, 1, 2);
        var r2c2 = new CellAddress(sheet.Id, 2, 2);

        var yellowStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 255, 0) });

        // Row 1: value 2 in col 1, blank-but-styled in col 2
        sheet.SetCell(r1c1, new NumberValue(2));
        // Row 2: value 1 in col 1, blank-but-styled in col 2 (will sort before row 1)
        sheet.SetCell(r2c1, new NumberValue(1));
        sheet.SetStyleOnly(r2c2.Row, r2c2.Col, yellowStyle);

        var range = new GridRange(r1c1, r2c2);
        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // Row 2 (value=1) sorted to row 1 -- style-only must follow
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetCell(1, 2).Should().BeNull();                       // still blank
        sheet.GetStyleOnly(1, 2).Should().Be(yellowStyle);           // style rode the row
        sheet.GetStyleOnly(2, 2).Should().BeNull();                   // row 2 is now plain

        command.Revert(ctx);

        // Style-only must be back at its original address (row 2 col 2)
        sheet.GetStyleOnly(2, 2).Should().Be(yellowStyle);
        sheet.GetStyleOnly(1, 2).Should().BeNull();
    }

    [Fact]
    public void SortCommand_Descending_KeepsBlanksLast()
    {
        // Regression: before the fix, negating the comparison for descending caused blank rows
        // to bubble to the TOP. Excel always keeps blanks at the bottom regardless of direction.
        // Ascending  [5, blank, 2] → [2, 5, blank]
        // Descending [5, blank, 2] → [5, 2, blank]
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(5));
        // row 2 is intentionally blank (no cell set)
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 1));

        // ── Ascending ────────────────────────────────────────────────────────
        var ascCmd = new SortCommand(sid, range, [new SortKey(0, true)]);
        ascCmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(5));
        sheet.GetCell(3, 1).Should().BeNull("blank row must stay last after ascending sort");

        ascCmd.Revert(ctx);

        // ── Descending ───────────────────────────────────────────────────────
        var descCmd = new SortCommand(sid, range, [new SortKey(0, false)]);
        descCmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetCell(3, 1).Should().BeNull("blank row must stay last after descending sort");
    }

    [Fact]
    public void SortCommand_Descending_KeepsErrorsLast()
    {
        // Errors (like blanks) must always sort to the bottom in both directions.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 2, 1), ErrorValue.DivByZero);
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(1));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 1));

        var cmd = new SortCommand(sid, range, [new SortKey(0, false)]);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(3, 1).Should().Be(ErrorValue.DivByZero, "error must stay last after descending sort");
    }

    private static void SetStyledRow(Sheet sheet, uint row, string label, StyleId styleId)
    {
        var keyCell = Cell.FromValue(new TextValue(label));
        keyCell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), keyCell);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
    }

}
