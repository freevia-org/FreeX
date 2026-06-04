using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void RenameStructuredTableCommand_UpdatesNameAndUndoRestoresPreviousMetadata()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            PackagePart = "xl/tables/table7.xml",
            NativeAttributes = new Dictionary<string, string> { ["published"] = "1" },
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status", TotalsRowFunction: "count")
            },
            FilterColumns =
            {
                new StructuredTableFilterColumnModel(1, ["Open"])
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "  Revenue2026  ");

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var renamed = sheet.StructuredTables.Should().ContainSingle().Subject;
        renamed.Should().NotBeSameAs(table);
        renamed.Name.Should().Be("Revenue2026");
        renamed.DisplayName.Should().Be("Revenue2026");
        renamed.Range.Should().Be(table.Range);
        renamed.StyleName.Should().Be(table.StyleName);
        renamed.PackagePart.Should().Be(table.PackagePart);
        renamed.NativeAttributes.Should().BeSameAs(table.NativeAttributes);
        renamed.Columns.Should().Equal(table.Columns);
        renamed.FilterColumns.Should().Equal(table.FilterColumns);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    [Fact]
    public void RenameStructuredTableCommand_RejectsInvalidDuplicateNamedRangeAndProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        other.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "Inventory",
            DisplayName = "Inventory",
            Range = new GridRange(new CellAddress(other.Id, 1, 1), new CellAddress(other.Id, 2, 2))
        });
        wb.DefineNamedRange(
            "Budget",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
        var ctx = new TestCommandContext(wb);

        new RenameStructuredTableCommand(sheet.Id, table.Id, "A1").Apply(ctx).Success.Should().BeFalse();
        new RenameStructuredTableCommand(sheet.Id, table.Id, "Inventory").Apply(ctx).Success.Should().BeFalse();
        new RenameStructuredTableCommand(sheet.Id, table.Id, "Budget").Apply(ctx).Success.Should().BeFalse();

        sheet.IsProtected = true;
        var protectedOutcome = new RenameStructuredTableCommand(sheet.Id, table.Id, "ProtectedRename").Apply(ctx);

        protectedOutcome.Success.Should().BeFalse();
        protectedOutcome.ErrorMessage.Should().Contain("protected");
        sheet.StructuredTables.Single().Name.Should().Be("Sales");
    }
}
