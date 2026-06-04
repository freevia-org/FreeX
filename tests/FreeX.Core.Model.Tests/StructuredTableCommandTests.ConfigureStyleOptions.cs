using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void ConfigureStructuredTableStyleOptionsCommand_UpdatesFlagsAndUndoRestoresPreviousTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Sales",
            DisplayName = "SalesDisplay",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            HeaderRowCount = 1,
            TotalsRowCount = 1,
            InsertRow = false,
            InsertRowShift = true,
            Published = true,
            Comment = "Loaded table",
            StyleName = "TableStyleMedium2",
            ShowFirstColumn = false,
            ShowLastColumn = true,
            ShowRowStripes = true,
            ShowColumnStripes = false,
            PackagePart = "/xl/tables/table1.xml",
            NativeSortStateXml = "<sortState/>",
            NativeAttributes = new Dictionary<string, string> { ["ref"] = "A1:B5" },
            NativeChildXmls = ["<extLst/>"],
            NativeAutoFilterAttributes = new Dictionary<string, string> { ["ref"] = "A1:B5" },
            NativeAutoFilterChildXmls = ["<filterColumn colId=\"0\"/>"],
            NativeStyleInfoAttributes = new Dictionary<string, string> { ["name"] = "TableStyleMedium2" },
            NativeStyleInfoChildXmls = ["<ext/>"],
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", NativeAttributes: new Dictionary<string, string> { ["id"] = "1" }),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            },
            FilterColumns =
            {
                new StructuredTableFilterColumnModel(0, ["North"], IncludeBlank: true)
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new ConfigureStructuredTableStyleOptionsCommand(
            sheet.Id,
            table.Id,
            showFirstColumn: true,
            showLastColumn: false,
            showRowStripes: false,
            showColumnStripes: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        configured.Should().NotBeSameAs(table);
        configured.ShowFirstColumn.Should().BeTrue();
        configured.ShowLastColumn.Should().BeFalse();
        configured.ShowRowStripes.Should().BeFalse();
        configured.ShowColumnStripes.Should().BeTrue();
        configured.Name.Should().Be(table.Name);
        configured.DisplayName.Should().Be(table.DisplayName);
        configured.Range.Should().Be(table.Range);
        configured.HasAutoFilter.Should().BeTrue();
        configured.TotalsRowShown.Should().BeTrue();
        configured.StyleName.Should().Be(table.StyleName);
        configured.PackagePart.Should().Be(table.PackagePart);
        configured.NativeSortStateXml.Should().Be(table.NativeSortStateXml);
        configured.NativeAttributes.Should().BeSameAs(table.NativeAttributes);
        configured.NativeChildXmls.Should().BeSameAs(table.NativeChildXmls);
        configured.NativeAutoFilterAttributes.Should().BeSameAs(table.NativeAutoFilterAttributes);
        configured.NativeAutoFilterChildXmls.Should().BeSameAs(table.NativeAutoFilterChildXmls);
        configured.NativeStyleInfoAttributes.Should().BeSameAs(table.NativeStyleInfoAttributes);
        configured.NativeStyleInfoChildXmls.Should().BeSameAs(table.NativeStyleInfoChildXmls);
        configured.Columns.Should().Equal(table.Columns);
        configured.FilterColumns.Should().Equal(table.FilterColumns);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    [Fact]
    public void ConfigureStructuredTableStyleOptionsCommand_CanUpdateStyleFilterAndTotalsMetadata()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var command = new ConfigureStructuredTableStyleOptionsCommand(
            sheet.Id,
            table.Id,
            showFirstColumn: table.ShowFirstColumn,
            showLastColumn: table.ShowLastColumn,
            showRowStripes: table.ShowRowStripes,
            showColumnStripes: table.ShowColumnStripes,
            styleName: "TableStyleDark4",
            updateStyleName: true,
            hasAutoFilter: false,
            totalsRowShown: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        configured.StyleName.Should().Be("TableStyleDark4");
        configured.HasAutoFilter.Should().BeFalse();
        configured.TotalsRowShown.Should().BeTrue();
        configured.Columns.Should().Equal(table.Columns);
        configured.FilterColumns.Should().Equal(table.FilterColumns);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    [Fact]
    public void ConfigureStructuredTableStyleOptionsCommand_RejectsMissingTableWithoutChangingTables()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var outcome = new ConfigureStructuredTableStyleOptionsCommand(
            sheet.Id,
            tableId: 99,
            showFirstColumn: true,
            showLastColumn: true,
            showRowStripes: false,
            showColumnStripes: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("not found");
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    [Fact]
    public void ConfigureStructuredTableStyleOptionsCommand_RejectsProtectedSheetWithoutChangingTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var outcome = new ConfigureStructuredTableStyleOptionsCommand(
            sheet.Id,
            table.Id,
            showFirstColumn: true,
            showLastColumn: true,
            showRowStripes: false,
            showColumnStripes: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }
}
