using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesEmptyValueTextRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotEmptyValueOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            StyleName = "PivotStyleLight16"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: "N/A",
            updateEmptyValueText: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EmptyValueText.Should().Be("N/A");
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new TextValue("N/A"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new TextValue("N/A"));

        command.Revert(ctx);

        pivot.EmptyValueText.Should().BeNull();
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new NumberValue(0));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesEmptyValueTextWhenCallerOmitsIt()
    {
        var workbook = new Workbook("PivotEmptyValueOptionsCompatibilityTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            StyleName = "PivotStyleLight16",
            EmptyValueText = "-"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: false,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EmptyValueText.Should().Be("-");
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new TextValue("-"));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesErrorCaptionAndUndoRestores()
    {
        var workbook = new Workbook("PivotErrorCaptionOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            StyleName = "PivotStyleLight16",
            ErrorCaption = "(old error)"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            errorCaption: "  #VALUE!  ",
            updateErrorCaption: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.ErrorCaption.Should().Be("#VALUE!");

        command.Revert(ctx);

        pivot.ErrorCaption.Should().Be("(old error)");
    }
}
