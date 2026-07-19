using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R49-commands-outline-group-3-1: Data &gt; Subtotal's "Summary below data" choice must drive
/// <see cref="Sheet.OutlineSummaryBelow"/> (the same flag Data &gt; Outline &gt; Settings writes and
/// Group/Ungroup/Collapse read), so later Collapse Group anchors to the row physically adjacent to
/// the hidden detail block instead of an unrelated group's total row, and the saved outlinePr
/// direction agrees with the actual row layout.
/// </summary>
public sealed class SubtotalOutlineSummaryBelowCommandTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void SubtotalCommand_SummaryAboveData_SetsOutlineSummaryBelowFalseSoCollapseAnchorsAboveTheBlock()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffset: 1,
            summaryBelowData: false);
        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Unchecking "Summary below data" must be reflected on the sheet itself, not just in the
        // one-off row layout this command wrote.
        sheet.OutlineSummaryBelow.Should().BeFalse();

        // Final layout: 1=header, 2=Grand Total, 3=East Total, 4-5=East detail, 6=West Total, 7-8=West detail.
        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(6, 1).Should().Be(new TextValue("West Total"));

        // Collapsing the East detail block (rows 4-5) must anchor to row 3 (East Total, the row
        // physically adjacent to the hidden block) -- not row 6 (West Total, an unrelated group's
        // total row), which is what happens when OutlineSummaryBelow is left at its true/absent
        // default while the actual rows are laid out summary-above.
        new CollapseRowGroupCommand(sheet.Id, level: 1, selectionStart: 4, selectionEnd: 4)
            .Apply(context).Success.Should().BeTrue();

        sheet.CollapsedAnchorRows.Should().Contain(3u);
        sheet.CollapsedAnchorRows.Should().NotContain(6u);
        sheet.IsRowEffectivelyHidden(4).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(5).Should().BeTrue();
    }

    [Fact]
    public void SubtotalCommand_SummaryBelowData_StillAnchorsCollapseBelowTheBlock()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        // Default (checked) "Summary below data" -- the previously-correct case, since Excel's
        // default direction already matched GroupRowsCommand's own `?? true` fallback.
        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);
        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.OutlineSummaryBelow.Should().BeTrue();

        // Final layout: 1=header, 2-3=East detail, 4=East Total, 5-6=West detail, 7=West Total, 8=Grand Total.
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));

        new CollapseRowGroupCommand(sheet.Id, level: 1, selectionStart: 2, selectionEnd: 2)
            .Apply(context).Success.Should().BeTrue();

        sheet.CollapsedAnchorRows.Should().Contain(4u);
        sheet.IsRowEffectivelyHidden(2).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();
    }
}
