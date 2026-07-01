using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

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
    [InlineData(1, 1, 4, 2, true)]
    [InlineData(1, 1, 1, 2, false)]
    public void IsValidSource_RequiresHeaderAndDataRow(uint r1, uint c1, uint r2, uint c2, bool expected)
    {
        var sheet = CreateSheet();

        PivotCreatePlanner.IsValidSource(Range(sheet, r1, c1, r2, c2)).Should().Be(expected);
    }

    [Fact]
    public void CreateSourceRangePlan_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedCell = Address(sheet, 3, 2);

        var plan = PivotCreatePlanner.CreateSourceRangePlan(sheet, new GridRange(selectedCell, selectedCell));

        plan.IsValid.Should().BeTrue();
        plan.SourceRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
        plan.Error.Should().Be(PivotCreateSourceRangeError.None);
    }

    [Fact]
    public void CreateSourceRangePlan_RejectsBlankHeaderCells()
    {
        var sheet = CreateSheetWithList();
        sheet.ClearCell(1, 2);

        var plan = PivotCreatePlanner.CreateSourceRangePlan(
            sheet,
            new GridRange(Address(sheet, 3, 2), Address(sheet, 3, 2)));

        plan.IsValid.Should().BeFalse();
        plan.SourceRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
        plan.Error.Should().Be(PivotCreateSourceRangeError.MissingHeaders);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(1, 4)]
    public void CreateSourceRangePlan_RejectsOneDimensionalRegions(uint rows, uint columns)
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= rows; row++)
        {
            for (uint col = 1; col <= columns; col++)
                sheet.SetCell(Address(sheet, row, col), new NumberValue(row + col));
        }

        var selectedCell = Address(sheet, 1, 1);

        var plan = PivotCreatePlanner.CreateSourceRangePlan(sheet, new GridRange(selectedCell, selectedCell));

        plan.IsValid.Should().BeFalse();
        plan.Error.Should().Be(PivotCreateSourceRangeError.MinimumShape);
    }

    [Fact]
    public void FormatRange_AndDefaultDestination_UseQuotedSheetReferences()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sales Q1");
        var range = Range(sheet, 1, 1, 20, 4);

        PivotCreatePlanner.FormatRange(workbook, sheet.Id, range).Should().Be("'Sales Q1'!A1:D20");
        PivotCreatePlanner.FormatDefaultDestination(workbook, sheet.Id, range).Should().Be("'Sales Q1'!F1");
    }

    [Fact]
    public void ReadFields_ReadsHeadersAndNumericFlags()
    {
        var sheet = SeedRegionSales();

        var fields = PivotCreatePlanner.ReadFields(sheet, Range(sheet, 1, 1, 4, 2));

        fields.Select(field => field.Header).Should().Equal("Region", "Sales");
        fields.Select(field => field.IsNumeric).Should().Equal(false, true);
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
    public void DefaultRoles_OneColumn_UsesItAsValueField()
    {
        var roles = PivotCreatePlanner.DefaultRoles(
        [
            new PivotCreatePlanner.SourceField(0, "Sales", IsNumeric: true)
        ]);

        PivotCreatePlanner.RowIndexes(roles).Should().BeEmpty();
        PivotCreatePlanner.ValueIndexes(roles).Should().Equal(0);
    }

    [Fact]
    public void CreateDefaultLayout_UsesSharedRoleDefaults()
    {
        var sheet = SeedRegionSales();

        var layout = PivotCreatePlanner.CreateDefaultLayout(sheet, Range(sheet, 1, 1, 4, 2));

        layout.RowFieldIndexes.Should().Equal(0);
        layout.DataFieldIndexes.Should().Equal(1);
    }

    [Fact]
    public void ChooseDefaultDataField_UsesFirstNumericOrDateColumnAfterHeader()
    {
        var sheet = SeedRegionSales();

        PivotCreatePlanner.ChooseDefaultDataField(sheet, Range(sheet, 1, 1, 4, 2)).Should().Be(1);
    }

    [Fact]
    public void SuggestName_IsUniqueAcrossWorkbook()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("Sheet1");
        var second = workbook.AddSheet("Sheet2");
        first.PivotTables.Add(new PivotTableModel { Name = "PivotTable1" });
        second.PivotTables.Add(new PivotTableModel { Name = "pivottable2" });

        PivotCreatePlanner.SuggestName(workbook).Should().Be("PivotTable3");
    }

    [Fact]
    public void SuggestName_ForSheet_PreservesHostScopedNaming()
    {
        var sheet = CreateSheet();
        sheet.PivotTables.Add(new PivotTableModel { Name = "PivotTable1" });
        sheet.PivotTables.Add(new PivotTableModel { Name = "pivottable2" });

        PivotCreatePlanner.SuggestName(sheet).Should().Be("PivotTable3");
    }

    [Fact]
    public void BuildCommand_NullTarget_BuildsNewWorksheetCommand()
    {
        var sheet = SeedRegionSales();
        var source = Range(sheet, 1, 1, 4, 2);

        var command = PivotCreatePlanner.BuildCommand(
            source, "PivotTable1", [0], [1], sheet.Id, target: null);

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
            source, "PivotTable1", [0], [1], sheet.Id, target);

        command.Should().BeOfType<AddPivotTableCommand>();
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PivotTables.Should().ContainSingle().Which.Name.Should().Be("PivotTable1");
    }

    [Fact]
    public void CreateRecommendedLayouts_UsesNumericFieldsAndDistinctRowFields()
    {
        var sheet = CreateSheetWithList();

        var layouts = PivotCreatePlanner.CreateRecommendedLayouts(
            sheet,
            new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));

        layouts.Should().Contain(layout =>
            layout.Title == "Sum of Score by Name" &&
            layout.RowFieldIndexes.SequenceEqual(new[] { 0 }) &&
            layout.DataFieldIndexes.SequenceEqual(new[] { 1 }));
    }

    private static Sheet CreateSheetWithList()
    {
        var sheet = CreateSheet();
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Team"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("East"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("West"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("North"));
        return sheet;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
