using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    // R125-commands-undo-byte-budget-further: RefreshPivotTableCommand ("pivot refresh") captures
    // a (Address, Cell?) snapshot per cell of the pivot's previously-rendered TargetRange before
    // Apply overwrites it, but fell back to the flat 200-byte IEstimatesMemory default regardless
    // of how large the rendered pivot output was.
    [Fact]
    public void RefreshPivotTableCommand_ImplementsIEstimatesMemory_ScalingWithTargetRangeCellCountAfterApply()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E5"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        // Pre-populate the target range that Refresh will snapshot before overwriting.
        for (uint r = 3; r <= 5; r++)
            for (uint c = 4; c <= 5; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new TextValue("old"));

        IWorkbookCommand command = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        var estimator = command as IEstimatesMemory;
        estimator.Should().NotBeNull("RefreshPivotTableCommand's undo snapshot is a full per-cell capture of the pivot's previously-rendered range");

        estimator!.EstimatedBytes.Should().Be(0, "nothing has been captured before Apply runs");

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        estimator.EstimatedBytes.Should().BeGreaterThan(0, "the pre-refresh target range had occupied cells that must have been captured for undo");
    }
}
