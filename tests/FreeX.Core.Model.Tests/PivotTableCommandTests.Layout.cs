using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableLayoutCommand_ReplacesFieldsRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotLayoutCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(1)],
            columnFields: [],
            pageFields: [new PivotFieldModel(0, SelectedItem: "A")],
            dataFields: [new PivotDataFieldModel(2, "Count of Amount", "count")]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(1);
        pivot.PageFields.Should().ContainSingle().Which.SelectedItem.Should().Be("A");
        pivot.DataFields.Should().ContainSingle().Which.SummaryFunction.Should().Be("count");
        sheet.GetCell(Addr(sheet, "E6"))!.Value.Should().Be(new TextValue("Q1"));
        sheet.GetCell(Addr(sheet, "F6"))!.Value.Should().Be(new NumberValue(1));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivot.PageFields.Should().BeEmpty();
        pivot.DataFields.Should().ContainSingle().Which.SummaryFunction.Should().Be("sum");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_PreservesFieldDropDownMetadataAndUndoRestores()
    {
        var workbook = new Workbook("PivotFieldDropDownMetadataCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, ShowDropDowns: true));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [],
            columnFields: [],
            pageFields: [new PivotFieldModel(0, SelectedItem: "A", MultipleItemSelectionAllowed: false, ShowDropDowns: false)],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]);

        command.Apply(ctx).Success.Should().BeTrue();

        var pageField = pivot.PageFields.Should().ContainSingle().Subject;
        pageField.SelectedItem.Should().Be("A");
        pageField.MultipleItemSelectionAllowed.Should().BeFalse();
        pageField.ShowDropDowns.Should().BeFalse();

        command.Revert(ctx);

        pivot.PageFields.Should().BeEmpty();
        pivot.RowFields.Should().ContainSingle().Which.ShowDropDowns.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_PrunesAndRemapsStaleFiltersAndSorts()
    {
        var workbook = new Workbook("PivotLayoutViewStateCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(3));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D3"),
            TargetRange = Range(sheet, "F3", "J8")
        };
        var amountField = new PivotDataFieldModel(2, "Sum of Amount", "sum");
        var unitsField = new PivotDataFieldModel(3, "Sum of Units", "sum");
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(amountField);
        pivot.DataFields.Add(unitsField);
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "E"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(1, PivotLabelFilterKind.Equals, "Q1"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 5, SourceFieldIndex: 0));
        pivot.ValueFilters.Add(new PivotValueFilterModel(1, PivotValueFilterKind.LessThan, ComparisonValue: 5, SourceFieldIndex: 1));
        pivot.ValueFilters.Add(new PivotValueFilterModel(1, PivotValueFilterKind.Top, Count: 1));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 1));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Ascending, DataFieldIndex: 0, FieldIndex: 0));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0)],
            columnFields: [],
            pageFields: [],
            dataFields: [unitsField, amountField]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.LabelFilters.Should().Equal(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "E"));
        pivot.ValueFilters.Should().Equal(
            new PivotValueFilterModel(1, PivotValueFilterKind.GreaterThan, ComparisonValue: 5, SourceFieldIndex: 0),
            new PivotValueFilterModel(0, PivotValueFilterKind.Top, Count: 1));
        pivot.Sorts.Should().Equal(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Ascending, DataFieldIndex: 1, FieldIndex: 0));

        command.Revert(ctx);

        pivot.RowFields.Select(field => field.SourceFieldIndex).Should().Equal(0, 1);
        pivot.DataFields.Should().Equal(amountField, unitsField);
        pivot.LabelFilters.Should().HaveCount(2);
        pivot.ValueFilters.Should().HaveCount(3);
        pivot.Sorts.Should().HaveCount(2);
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_AllowsValuesOnlyLayout()
    {
        var workbook = new Workbook("PivotValuesOnlyLayoutCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Sum of Amount"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new NumberValue(30));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Category"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_UpdatesBoundPivotChartDataRange()
    {
        var workbook = new Workbook("PivotLayoutChartSyncTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        sheet.Charts.Add(new ChartModel
        {
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1,
            DataRange = Range(sheet, "E3", "F6")
        });

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0), new PivotFieldModel(1)],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(2, "Sum of Amount", "sum")]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        sheet.Charts[0].PivotCacheId.Should().Be(pivot.CacheId);
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ProtectedPivotLayoutCommandTest");
        sheet.IsProtected = true;

        var outcome = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            pivot.Name,
            rowFields: [],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_AllowsProtectedSheetWithUsePivotReportsPermission()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ProtectedPivotLayoutCommandTest");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);

        var outcome = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            pivot.Name,
            rowFields: [],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        pivot.RowFields.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void ConfigurePivotTableCalculatedItemsCommand_ReplacesGroupingAndCalculatedDefinitions()
    {
        var workbook = new Workbook("PivotCalculatedItemsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(3));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Units", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        var ctx = new TestCommandContext(workbook);

        var command = new ConfigurePivotTableCalculatedItemsCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(1, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10)],
            columnFields: [],
            pageFields: [],
            calculatedFields: [new PivotCalculatedFieldModel("Revenue", "Amount*Units")],
            calculatedItems: [new PivotCalculatedItemModel(1, "Small + Large", "10+20")]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().ContainSingle().Which.Should().Be(
            new PivotFieldModel(1, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.CalculatedFields.Should().ContainSingle().Which.Should().Be(new PivotCalculatedFieldModel("Revenue", "Amount*Units"));
        pivot.CalculatedItems.Should().ContainSingle().Which.Should().Be(new PivotCalculatedItemModel(1, "Small + Large", "10+20"));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.Should().Be(new PivotFieldModel(1));
        pivot.CalculatedFields.Should().BeEmpty();
        pivot.CalculatedItems.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurePivotTableCalculatedItemsCommand_RejectsInvalidCalculatedDefinitions()
    {
        var workbook = new Workbook("PivotCalculatedItemsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        var ctx = new TestCommandContext(workbook);

        var command = new ConfigurePivotTableCalculatedItemsCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(2)],
            columnFields: [],
            pageFields: [],
            calculatedFields: [new PivotCalculatedFieldModel("", "Amount*2")],
            calculatedItems: []);

        command.Apply(ctx).Success.Should().BeFalse();

        pivot.RowFields.Should().ContainSingle().Which.Should().Be(new PivotFieldModel(0));
        pivot.CalculatedFields.Should().BeEmpty();
    }
}
