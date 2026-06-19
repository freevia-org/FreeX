namespace FreeW.Core.Model.Tests;

/// <summary>
/// Pure-model coverage for the Table Properties surface added for Word's Table Properties dialog: the
/// new table / row / cell properties default to "unset" (so existing tables are unaffected) and the
/// <see cref="TableCellMargins"/> record carries Word's defaults.
/// </summary>
public class TablePropertiesModelTests
{
    [Fact]
    public void NewTable_HasUnsetProperties_ByDefault()
    {
        var table = Table.Create(1, 1);

        table.PreferredWidthPt.Should().BeNull();
        table.Alignment.Should().Be(TableAlignment.Left);
        table.IndentFromLeftPt.Should().BeNull();
        table.TextWrapping.Should().BeFalse();
        table.DefaultCellMargins.Should().BeNull();
        table.CellSpacingPt.Should().BeNull();
    }

    [Fact]
    public void NewRow_AllowsBreakAndHasAutoHeight_ByDefault()
    {
        var row = new TableRow();

        row.HeightPt.Should().BeNull();
        row.HeightRule.Should().Be(TableRowHeightRule.Auto);
        row.AllowBreakAcrossPages.Should().BeTrue();
    }

    [Fact]
    public void NewCell_IsTopAlignedWithNoMarginOverride_ByDefault()
    {
        var cell = new TableCell("x");

        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
        cell.Margins.Should().BeNull();
    }

    [Fact]
    public void TableCellMargins_Default_MatchesWordDefaults()
    {
        var margins = TableCellMargins.Default;

        margins.TopPt.Should().Be(0);
        margins.BottomPt.Should().Be(0);
        margins.LeftPt.Should().Be(5.4);
        margins.RightPt.Should().Be(5.4);
    }
}
