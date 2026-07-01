using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableDesignCommandPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void TryGetActiveStructuredTable_ChoosesSmallestContainingTable()
    {
        var (workbook, sheet, _) = BuildWorkbook();
        var outer = new StructuredTableModel
        {
            Id = 2,
            Name = "Outer",
            DisplayName = "Outer",
            Range = Range(sheet.Id, 1, 1, 20, 10),
        };
        sheet.StructuredTables.Insert(0, outer);

        var ok = TableDesignCommandPlanner.TryGetActiveStructuredTable(
            sheet,
            new CellAddress(sheet.Id, 2, 2),
            out var table);

        ok.Should().BeTrue();
        table.Id.Should().Be(1);
    }

    [Fact]
    public void GetDisplayName_FallsBackToInternalName()
    {
        TableDesignCommandPlanner.GetDisplayName(new StructuredTableModel
        {
            Name = "Table1",
            DisplayName = "",
        }).Should().Be("Table1");
    }

    [Fact]
    public void BuildRenameCommand_AppliesValidatedName()
    {
        var (workbook, sheet, table) = BuildWorkbook();
        var command = TableDesignCommandPlanner.BuildRenameCommand(
            sheet.Id,
            table,
            new TableNameValues("Revenue"));

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.StructuredTables[0].Name.Should().Be("Revenue");
        sheet.StructuredTables[0].DisplayName.Should().Be("Revenue");
    }

    [Fact]
    public void BuildResizeCommand_ReappliesKnownGalleryStyle()
    {
        var (workbook, sheet, table) = BuildWorkbook(styleName: TableStyleGalleryPlanner.DefaultStyleName);
        var newRange = Range(sheet.Id, 1, 1, 8, 3);

        var command = TableDesignCommandPlanner.BuildResizeCommand(sheet.Id, table, newRange, workbook.Theme);

        command.Should().BeOfType<CompositeWorkbookCommand>();
        command.Label.Should().Be(TableDesignCommandPlanner.ResizeTableCommandLabel);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables[0].Range.Should().Be(newRange);
    }

    [Fact]
    public void BuildResizeCommand_UsesPlainResizeForNonGalleryStyle()
    {
        var (workbook, sheet, table) = BuildWorkbook(styleName: "CustomTableStyle");

        var command = TableDesignCommandPlanner.BuildResizeCommand(
            sheet.Id,
            table,
            Range(sheet.Id, 1, 1, 8, 3),
            workbook.Theme);

        command.Should().BeOfType<ResizeStructuredTableCommand>();
    }

    [Fact]
    public void BuildStyleOptionsCommand_ReturnsNullWhenNothingChanges()
    {
        var (workbook, sheet, table) = BuildWorkbook();

        TableDesignCommandPlanner.BuildStyleOptionsCommand(sheet.Id, table, workbook.Theme)
            .Should().BeNull();
    }

    [Fact]
    public void BuildStyleOptionsCommand_CombinesTotalsRowWithKnownStyleReapply()
    {
        var (workbook, sheet, table) = BuildWorkbook(styleName: TableStyleGalleryPlanner.DefaultStyleName);

        var command = TableDesignCommandPlanner.BuildStyleOptionsCommand(
            sheet.Id,
            table,
            workbook.Theme,
            showFirstColumn: true,
            totalsRowShown: true);

        command.Should().BeOfType<CompositeWorkbookCommand>();
        command!.Label.Should().Be(TableDesignCommandPlanner.TableStyleOptionsCommandLabel);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var updated = sheet.StructuredTables.Single();
        updated.TotalsRowShown.Should().BeTrue();
        updated.ShowFirstColumn.Should().BeTrue();
    }

    [Fact]
    public void BuildStyleOptionsCommand_ReappliesCustomStylesForOptionChanges()
    {
        var (workbook, sheet, table) = BuildWorkbook(styleName: "CustomTableStyle");

        var command = TableDesignCommandPlanner.BuildStyleOptionsCommand(
            sheet.Id,
            table,
            workbook.Theme,
            showColumnStripes: true);

        command.Should().BeOfType<ReapplyStructuredTableStyleCommand>();
        command!.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables[0].ShowColumnStripes.Should().BeTrue();
    }

    [Fact]
    public void BuildApplyStyleCommand_UpdatesStyleName()
    {
        var (workbook, sheet, table) = BuildWorkbook();
        var option = TableStyleGalleryPlanner.GetOption(3, workbook.Theme);

        var command = TableDesignCommandPlanner.BuildApplyStyleCommand(sheet.Id, table, option);

        command.Should().BeOfType<ApplyStructuredTableStyleCommand>();
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables[0].StyleName.Should().Be(option.StyleName);
    }

    [Fact]
    public void BuildConvertToRangePlan_CapturesDisplayNameAndCommand()
    {
        var (_, sheet, table) = BuildWorkbook(displayName: "");

        var plan = TableDesignCommandPlanner.BuildConvertToRangePlan(sheet.Id, table);

        plan.TableDisplayName.Should().Be(table.Name);
        plan.Command.Should().BeOfType<ConvertStructuredTableToRangeCommand>();
    }

    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table) BuildWorkbook(
        string styleName = "",
        string displayName = "Table1")
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = displayName,
            Range = Range(sheet.Id, 1, 1, 4, 2),
            StyleName = styleName,
            HasAutoFilter = true,
            ShowRowStripes = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet, table);
    }

    private static GridRange Range(SheetId sheetId, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheetId, r1, c1), new CellAddress(sheetId, r2, c2));
}
