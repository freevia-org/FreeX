using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesFormatOptionsAndUndoRestores()
    {
        var workbook = new Workbook("PivotFormatOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            AutofitColumnsOnUpdate = true,
            PreserveFormattingOnUpdate = true
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
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.AutofitColumnsOnUpdate.Should().BeFalse();
        pivot.PreserveFormattingOnUpdate.Should().BeFalse();

        command.Revert(ctx);

        pivot.AutofitColumnsOnUpdate.Should().BeTrue();
        pivot.PreserveFormattingOnUpdate.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesCompactRowLabelIndentAndUndoRestores()
    {
        var workbook = new Workbook("PivotCompactIndentCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 1
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            reportLayout: PivotReportLayout.Compact,
            compactRowLabelIndent: 4);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.CompactRowLabelIndent.Should().Be(4);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "D4"))!.StyleId).IndentLevel.Should().Be(4);

        command.Revert(ctx);

        pivot.CompactRowLabelIndent.Should().Be(1);
    }
}
