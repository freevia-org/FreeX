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
        cell.WrapText.Should().BeTrue();
        cell.FitText.Should().BeFalse();
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

    [Fact]
    public void TableLayoutOperations_DistributeColumns_SetsSharedGridAndCellWidths()
    {
        var table = Table.Create(2, 3);
        table.ColumnWidthsPt.AddRange([60, 120, 180]);

        TableLayoutOperations.DistributeColumns(table).Should().BeTrue();

        table.ColumnWidthsPt.Should().Equal(120, 120, 120);
        table.Rows.SelectMany(row => row.Cells)
            .Should().AllSatisfy(cell => cell.WidthPt.Should().Be(120));
    }

    [Fact]
    public void TableLayoutOperations_SplitPreservesTableShellProperties()
    {
        var table = Table.Create(3, 2);
        table.Formatting = table.Formatting with { HeaderRow = true, BandedColumns = true };
        table.TableStyleId = "GridTable1Light";
        table.Borders = new TableBorders { Top = new TableBorderEdge(BorderLineStyle.Double, "1F4E79", 1.5) };
        table.PreferredWidthPt = 360;
        table.Alignment = TableAlignment.Center;
        table.DefaultCellMargins = new TableCellMargins(1, 6, 1, 6);
        table.AutoFit = AutoFitMode.Window;
        table.ColumnWidthsPt.AddRange([180, 180]);

        TableLayoutOperations.TryBuildSplitReplacement(table, 1, out var replacement)
            .Should().BeTrue();

        replacement.Should().HaveCount(3);
        var top = replacement[0].Should().BeOfType<Table>().Subject;
        var bottom = replacement[2].Should().BeOfType<Table>().Subject;
        top.Rows.Should().HaveCount(1);
        bottom.Rows.Should().HaveCount(2);
        bottom.TableStyleId.Should().Be("GridTable1Light");
        bottom.Borders.Should().Be(table.Borders);
        bottom.Formatting.BandedColumns.Should().BeTrue();
        bottom.PreferredWidthPt.Should().Be(360);
        bottom.Alignment.Should().Be(TableAlignment.Center);
        bottom.DefaultCellMargins.Should().Be(new TableCellMargins(1, 6, 1, 6));
        bottom.AutoFit.Should().Be(AutoFitMode.Window);
        bottom.ColumnWidthsPt.Should().Equal(180, 180);
    }
}
