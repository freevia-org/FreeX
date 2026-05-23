using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class CreateNamedRangesFromSelectionCommandTests
{
    [Fact]
    public void CreateFromTopRow_DefinesEachColumnNameOverRowsBelowLabels()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Net Sales");
        Set(sheet, 2, 1, "East");
        Set(sheet, 3, 1, "West");
        Set(sheet, 2, 2, 10);
        Set(sheet, 3, 2, 20);

        var command = new CreateNamedRangesFromSelectionCommand(
            new GridRange(Addr(sheet, 1, 1), Addr(sheet, 3, 2)),
            UseTopRow: true,
            UseLeftColumn: false,
            UseBottomRow: false,
            UseRightColumn: false);

        command.Apply(ctx).Success.Should().BeTrue();

        wb.NamedRanges.Should().ContainKey("Region");
        wb.NamedRanges["Region"].Should().Be(new GridRange(Addr(sheet, 2, 1), Addr(sheet, 3, 1)));
        wb.NamedRanges.Should().ContainKey("Net_Sales");
        wb.NamedRanges["Net_Sales"].Should().Be(new GridRange(Addr(sheet, 2, 2), Addr(sheet, 3, 2)));
    }

    [Fact]
    public void CreateFromLeftColumn_DefinesEachRowNameOverCellsToRightOfLabels()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        Set(sheet, 1, 1, "North");
        Set(sheet, 2, 1, "South");
        Set(sheet, 1, 2, 10);
        Set(sheet, 1, 3, 12);
        Set(sheet, 2, 2, 20);
        Set(sheet, 2, 3, 24);

        var command = new CreateNamedRangesFromSelectionCommand(
            new GridRange(Addr(sheet, 1, 1), Addr(sheet, 2, 3)),
            UseTopRow: false,
            UseLeftColumn: true,
            UseBottomRow: false,
            UseRightColumn: false);

        command.Apply(ctx).Success.Should().BeTrue();

        wb.NamedRanges["North"].Should().Be(new GridRange(Addr(sheet, 1, 2), Addr(sheet, 1, 3)));
        wb.NamedRanges["South"].Should().Be(new GridRange(Addr(sheet, 2, 2), Addr(sheet, 2, 3)));
    }

    [Fact]
    public void CreateFromSelection_CleansInvalidLabelsAndMakesDuplicatesUnique()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        Set(sheet, 1, 1, "2026 Sales");
        Set(sheet, 1, 2, "2026 Sales");
        Set(sheet, 1, 3, "A1");
        Set(sheet, 2, 1, 1);
        Set(sheet, 2, 2, 2);
        Set(sheet, 2, 3, 3);

        var command = new CreateNamedRangesFromSelectionCommand(
            new GridRange(Addr(sheet, 1, 1), Addr(sheet, 2, 3)),
            UseTopRow: true,
            UseLeftColumn: false,
            UseBottomRow: false,
            UseRightColumn: false);

        command.Apply(ctx).Success.Should().BeTrue();

        wb.NamedRanges.Keys.Should().Contain(["_2026_Sales", "_2026_Sales_2", "_A1"]);
    }

    [Fact]
    public void CreateFromSelection_ReplacesExistingNamesAndUndoRestoresPreviousCatalog()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var original = new GridRange(Addr(sheet, 10, 1), Addr(sheet, 10, 1));
        wb.DefineNamedRange("Sales", original, new NamedRangeMetadata("Sheet1", "Manual override"));
        Set(sheet, 1, 1, "Sales");
        Set(sheet, 2, 1, 99);

        var command = new CreateNamedRangesFromSelectionCommand(
            new GridRange(Addr(sheet, 1, 1), Addr(sheet, 2, 1)),
            UseTopRow: true,
            UseLeftColumn: false,
            UseBottomRow: false,
            UseRightColumn: false);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.NamedRanges["Sales"].Should().Be(new GridRange(Addr(sheet, 2, 1), Addr(sheet, 2, 1)));

        command.Revert(ctx);

        wb.NamedRanges.Should().ContainSingle();
        wb.NamedRanges["Sales"].Should().Be(original);
        wb.NamedRangeMetadataByName["Sales"].Should().Be(new NamedRangeMetadata("Sheet1", "Manual override"));
    }

    [Fact]
    public void CreateFromSelection_RejectsWhenNoLabelPositionIsSelected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var command = new CreateNamedRangesFromSelectionCommand(
            new GridRange(Addr(sheet, 1, 1), Addr(sheet, 2, 2)),
            UseTopRow: false,
            UseLeftColumn: false,
            UseBottomRow: false,
            UseRightColumn: false);

        command.Apply(ctx).Success.Should().BeFalse();
    }

    private static CellAddress Addr(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);
    private static void Set(Sheet sheet, uint row, uint col, string text) => sheet.SetCell(Addr(sheet, row, col), new TextValue(text));
    private static void Set(Sheet sheet, uint row, uint col, double number) => sheet.SetCell(Addr(sheet, row, col), new NumberValue(number));

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
