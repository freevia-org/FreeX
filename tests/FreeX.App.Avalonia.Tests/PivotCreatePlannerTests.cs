using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia.Pivot;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="PivotCreatePlanner"/> backing the Insert PivotTable dialog: source
/// validation, reading source fields (header + numeric flag), the default Row/Value assignment, and building
/// the Core add command (new worksheet vs in-place target). No running shell required.
/// </summary>
public sealed class PivotCreatePlannerTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static Sheet SeedRegionSales()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        return sheet;
    }

    [Theory]
    [InlineData(1, 1, 4, 2, true)]  // header + 3 data rows
    [InlineData(1, 1, 1, 2, false)] // header only, no data
    public void IsValidSource_RequiresHeaderAndDataRow(uint r1, uint c1, uint r2, uint c2, bool expected)
    {
        var sheet = CreateSheet();
        PivotCreatePlanner.IsValidSource(Range(sheet, r1, c1, r2, c2)).Should().Be(expected);
    }

    [Fact]
    public void ReadFields_ReadsHeadersAndNumericFlags()
    {
        var sheet = SeedRegionSales();

        var fields = PivotCreatePlanner.ReadFields(sheet, Range(sheet, 1, 1, 4, 2));

        fields.Select(f => f.Header).Should().Equal("Region", "Sales");
        fields.Select(f => f.IsNumeric).Should().Equal(false, true);
    }

    [Fact]
    public void DefaultRoles_TextColumnRow_NumericColumnsValue()
    {
        var sheet = SeedRegionSales();
        var fields = PivotCreatePlanner.ReadFields(sheet, Range(sheet, 1, 1, 4, 2));

        var roles = PivotCreatePlanner.DefaultRoles(fields);

        roles[0].Should().Be(PivotCreatePlanner.FieldRole.Row);
        roles[1].Should().Be(PivotCreatePlanner.FieldRole.Value);
        PivotCreatePlanner.RowIndexes(roles).Should().Equal(0);
        PivotCreatePlanner.ValueIndexes(roles).Should().Equal(1);
    }

    [Fact]
    public void DefaultRoles_AllText_FirstIsRow_LastIsValue()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("y"));
        var fields = PivotCreatePlanner.ReadFields(sheet, Range(sheet, 1, 1, 2, 2));

        var roles = PivotCreatePlanner.DefaultRoles(fields);

        PivotCreatePlanner.RowIndexes(roles).Should().Equal(0);
        PivotCreatePlanner.ValueIndexes(roles).Should().Equal(1);
    }

    [Fact]
    public void SuggestName_IsUniqueAcrossWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");

        PivotCreatePlanner.SuggestName(workbook).Should().Be("PivotTable1");
    }

    [Fact]
    public void BuildCommand_NullTarget_BuildsNewWorksheetCommand()
    {
        var sheet = SeedRegionSales();
        var source = Range(sheet, 1, 1, 4, 2);

        var command = PivotCreatePlanner.BuildCommand(
            source, "PivotTable1", new[] { 0 }, new[] { 1 }, sheet.Id, target: null);

        command.Should().BeOfType<AddPivotTableToNewWorksheetCommand>();
    }

    [Fact]
    public void BuildCommand_WithTarget_BuildsInPlaceCommand_AndCreatesPivotOnApply()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        var source = Range(sheet, 1, 1, 4, 2);
        var target = new CellAddress(sheet.Id, 6, 1);
        var command = PivotCreatePlanner.BuildCommand(
            source, "PivotTable1", new[] { 0 }, new[] { 1 }, sheet.Id, target);

        command.Should().BeOfType<AddPivotTableCommand>();
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PivotTables.Should().ContainSingle().Which.Name.Should().Be("PivotTable1");
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
