using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class TextToColumnsPlannerTests
{
    [Fact]
    public void SplitFixedWidthText_UsesSortedUniqueBreakPositions()
    {
        TextToColumnsPlanner.SplitFixedWidthText("East0042Open", [8, 4, 4])
            .Should()
            .Equal("East", "0042", "Open");
    }

    [Fact]
    public void BuildFixedWidthEdits_SplitsTextAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East0042Open"));

        var edits = TextToColumnsPlanner.BuildFixedWidthEdits(sheet, range, [4, 8]);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }
}
