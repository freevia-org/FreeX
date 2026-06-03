using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void ConvertStructuredTableToRangeCommand_RemovesOnlyTableMetadataAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        var lowerTable = new StructuredTableModel
        {
            Id = 8,
            Name = "Inventory",
            DisplayName = "Inventory",
            Range = new GridRange(new CellAddress(sheet.Id, 8, 1), new CellAddress(sheet.Id, 9, 2))
        };
        sheet.StructuredTables.Add(table);
        sheet.StructuredTables.Add(lowerTable);
        var ctx = new SimpleCtx(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(lowerTable);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Region"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("North"));

        command.Revert(ctx);

        sheet.StructuredTables.Should().Equal(table, lowerTable);
    }

    [Fact]
    public void ConvertStructuredTableToRangeCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(wb);

        var outcome = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }
}
