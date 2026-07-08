using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-13 bucket S6 findings:
///   - R13-hyperlinks-deep-3: SetHyperlinkCommand must clear stale RichTextRuns for the cell it
///     overwrites (and restore them on undo) — mirroring GroupedEditCellsCommand's handling.
///   - R13-meta-2: Sheet.ColumnFilterOwnedRows is column-keyed exactly like ActiveValueFilterColumns
///     and must shift on column insert/delete the same way, or a filter column's owned-hidden-row
///     bookkeeping is mis-attributed to whatever column ends up at the stale index.
/// </summary>
public sealed class FreeXR13S6Tests
{
    // ── R13-hyperlinks-deep-3 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SetHyperlinkCommand_RemovesStaleRichTextRunsOnApply_AndRestoresOnUndo()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("Hello World")));

        // "World" (chars 6..11) is bold — a second rich-text run inside the pre-hyperlink cell text.
        var originalRuns = new List<CellTextRun>
        {
            new("Hello ", Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
            new("World", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)
        };
        sheet.RichTextRuns[addr] = originalRuns;

        var command = new SetHyperlinkCommand(sheet.Id, addr, "http://x.com", "Go");
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("Go"));
        sheet.RichTextRuns.Should().NotContainKey(addr,
            "the stale rich-text runs' offsets were computed against the old 11-char text and no " +
            "longer line up with the new 2-char hyperlink display text, so they must not carry over");

        command.Revert(ctx);

        sheet.GetValue(addr).Should().Be(new TextValue("Hello World"));
        sheet.RichTextRuns.Should().ContainKey(addr);
        sheet.RichTextRuns[addr].Should().BeEquivalentTo(originalRuns);
    }

    // ── R13-meta-2 ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertColumns_ShiftsColumnFilterOwnedRowsKey_AndFixesDownstreamClear()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // Column C (3) holds numeric scores; a Top-1 filter keeps row 2 (score 10) and hides rows
        // 3 and 4, recording that ownership under Sheet.ColumnFilterOwnedRows[3].
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(5));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 2, count: 1, top: true)
            .Apply(ctx).Success.Should().BeTrue();
        sheet.ColumnFilterOwnedRows.Should().ContainKey(3);
        sheet.ColumnFilterOwnedRows[3].Should().BeEquivalentTo([3u, 4u]);
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);

        // Insert a column before B (2): the filtered column moves from C (3) to D (4). The
        // ownership key must shift in lockstep with sheet.ActiveValueFilterColumns's own shift.
        var insert = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        insert.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnFilterOwnedRows.Should().NotContainKey(3,
            "the stale key must not linger and get mis-attributed to the newly inserted blank column");
        sheet.ColumnFilterOwnedRows.Should().ContainKey(4);
        sheet.ColumnFilterOwnedRows[4].Should().BeEquivalentTo([3u, 4u]);

        // Undo must restore the ownership entry to its original column key.
        insert.Revert(ctx);

        sheet.ColumnFilterOwnedRows.Should().ContainKey(3);
        sheet.ColumnFilterOwnedRows[3].Should().BeEquivalentTo([3u, 4u]);
        sheet.ColumnFilterOwnedRows.Should().NotContainKey(4);

        // Re-apply the insert (no further undo) and prove the real downstream consequence: clearing
        // the filter through the column's new, shifted position must find its owned rows and unhide
        // them. Before the fix, the ownership key stayed stale at 3, so looking it up at the new
        // column D's index (4) found nothing and rows 3/4 stayed hidden forever.
        insert.Apply(ctx).Success.Should().BeTrue();

        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 3, count: 0, top: true)
            .Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void DeleteColumns_DropsDeletedColumnFilterOwnedRowsKeyAndShiftsSurvivorsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // Column B (2) is deleted outright; column D (4) survives and must shift down to column C (3).
        sheet.ColumnFilterOwnedRows[2] = [7u];
        sheet.ColumnFilterOwnedRows[4] = [8u, 9u];

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnFilterOwnedRows.Should().NotContainKey(2);
        sheet.ColumnFilterOwnedRows.Should().NotContainKey(4);
        sheet.ColumnFilterOwnedRows.Should().ContainKey(3);
        sheet.ColumnFilterOwnedRows[3].Should().BeEquivalentTo([8u, 9u]);

        command.Revert(ctx);

        sheet.ColumnFilterOwnedRows.Should().ContainKey(2);
        sheet.ColumnFilterOwnedRows[2].Should().BeEquivalentTo([7u]);
        sheet.ColumnFilterOwnedRows.Should().ContainKey(4);
        sheet.ColumnFilterOwnedRows[4].Should().BeEquivalentTo([8u, 9u]);
        sheet.ColumnFilterOwnedRows.Should().NotContainKey(3);
    }
}
