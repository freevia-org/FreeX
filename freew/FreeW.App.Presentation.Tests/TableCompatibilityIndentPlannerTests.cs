using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class TableCompatibilityIndentPlannerTests
{
    private static Table NewTable() => new();

    [Fact]
    public void LegacyMode_OutdentsALeftAlignedTableByTheDefaultCellPadding()
    {
        TableCompatibilityIndentPlanner.ResolveLeftOutdentPt(true, NewTable()).Should().Be(5.4);
    }

    [Fact]
    public void CurrentMode_DoesNotOutdent()
    {
        TableCompatibilityIndentPlanner.ResolveLeftOutdentPt(false, NewTable()).Should().Be(0);
    }

    [Theory]
    [InlineData(TableAlignment.Center)]
    [InlineData(TableAlignment.Right)]
    public void LegacyMode_DoesNotOutdentAlignedTables(TableAlignment alignment)
    {
        var table = NewTable();
        table.Alignment = alignment;

        TableCompatibilityIndentPlanner.ResolveLeftOutdentPt(true, table).Should().Be(0);
    }

    [Fact]
    public void LegacyMode_UsesTheTableDefaultPaddingWhenSet()
    {
        var table = NewTable();
        table.DefaultCellMargins = new TableCellMargins(LeftPt: 7.2);

        TableCompatibilityIndentPlanner.ResolveLeftOutdentPt(true, table).Should().Be(7.2);
    }
}
